# Home Assistant

GarageStack and [Home Assistant](https://www.home-assistant.io) can share the same car data. The SAIC MQTT gateway inside GarageStack already publishes [Home Assistant MQTT discovery](https://www.home-assistant.io/integrations/mqtt/#mqtt-discovery) messages, so all Home Assistant needs is a login on GarageStack's Mosquitto broker. Your car then shows up as a device with sensors (battery, range, tyre pressures, doors, location and more) and controls (lock, climate, charging), with no custom component or YAML.

**Don't install a separate MG / SAIC integration in Home Assistant as well.** The MG iSmart API allows one active session per account, so a second client logging in with the same account keeps kicking GarageStack out (see [MG iSmart account and session limits](../README.md#mg-ismart-account-and-session-limits)). Sharing the broker means one login and one set of polls against the MG cloud, used by both.

---

## How it works

```text
  SAIC / MG cloud
        │
  saic-mqtt-gateway ── publishes ──►  saic/<account>/vehicles/<vin>/...    (telemetry)
        │                             homeassistant/.../config            (discovery, retained)
        ▼
    Mosquitto ◄──── GarageStack Worker and Api   (internal login, full access)
        ▲
        └──────── Home Assistant                 (HA_MQTT_USERNAME, restricted)
```

- Home Assistant reads the retained discovery configs under `homeassistant/`, creates the entities, and follows their state on `saic/...`.
- Its controls publish to the gateway's command topics (`saic/.../set`), exactly like GarageStack's own buttons do.
- When Home Assistant restarts it posts `online` to `homeassistant/status`, and the gateway answers by publishing discovery again.

---

## Setup

### 1. Create the Home Assistant broker login

Set both variables in `.env` (Docker Compose) or as container environment variables (all-in-one). The Unraid template has them as **Home Assistant MQTT Username** and **Home Assistant MQTT Password** among the advanced settings.

```bash
HA_MQTT_USERNAME=homeassistant
HA_MQTT_PASSWORD=<openssl rand -hex 32>
```

Then restart the broker so it picks them up. Mosquitto rebuilds its password and ACL files on every start:

- Docker Compose: `docker compose up -d mosquitto`
- All-in-one: restart the container

This login is deliberately limited. It cannot see or publish anything else on the broker:

| Topic | Access | Why |
| --- | --- | --- |
| `saic/#` | read and write | Car telemetry, plus the `/set` command topics behind the controls |
| `homeassistant/#` | read | The gateway's discovery configs |
| `homeassistant/status` | write | Home Assistant's online/offline message |

### 2. Make the broker reachable from Home Assistant

The broker speaks plain MQTT without TLS. Keep it on your LAN and never forward port 1883 on your router.

| Where Home Assistant runs | Broker address to use | What to change |
| --- | --- | --- |
| Container with `network_mode: host`, on the same machine as GarageStack (Compose) | `127.0.0.1`, port `1883` | Nothing, the broker already listens on localhost |
| Container on the same Docker network as GarageStack's Mosquitto (Compose) | `mosquitto`, port `1883` | Attach it to that network, e.g. `docker network connect garagestack_default homeassistant` |
| Anywhere else: Home Assistant OS VM, Raspberry Pi, another server (Compose) | The Docker host's LAN IP, port `MQTT_EXTERNAL_PORT` (`1883`) | Set `MQTT_BIND_ADDRESS` in `.env` to the host's LAN IP (or `0.0.0.0`), then `docker compose up -d mosquitto` |
| Anywhere, with the all-in-one container | The Docker host's LAN IP, port `1883` | Publish the port: `-p 1883:1883`, or fill in **MQTT Port** in the Unraid template |

### 3. Add the MQTT integration in Home Assistant

1. Go to **Settings > Devices & services > Add integration** and pick **MQTT**.
2. Enter the broker address and port from step 2, plus `HA_MQTT_USERNAME` and `HA_MQTT_PASSWORD`.
3. Leave discovery enabled with the default prefix, `homeassistant`.

Two kinds of device appear: **SAIC Python MQTT Gateway**, with account diagnostics such as the last login, and one device per car. If nothing shows up within a minute, restart the `saic-mqtt-gateway` container (all-in-one: the whole container) so it publishes discovery again.

---

## Home Assistant already has a broker

Home Assistant connects to one MQTT broker only. If you already run one, for example the Mosquitto broker add-on used by Zigbee2MQTT, keep it and bridge the car's topics into it instead. Your existing MQTT integration stays exactly as it is.

Do steps 1 and 2 above, then skip step 3. The bridge connects from the machine running Home Assistant's broker to GarageStack's broker, so in step 2 that usually means the "anywhere else" row.

This is the bridge configuration. `address` is the machine running **GarageStack** (the broker address from step 2), not Home Assistant, and the username and password are your `HA_MQTT_USERNAME` and `HA_MQTT_PASSWORD`:

```text
connection garagestack
address 192.168.1.100:1883
remote_username homeassistant
remote_password <HA_MQTT_PASSWORD>
remote_clientid ha-bridge-garagestack
topic saic/# both 0
topic homeassistant/# in 0
topic homeassistant/status out 0
```

Your existing broker's own discovery messages stay local: only `homeassistant/status` is sent to GarageStack.

### With the Mosquitto broker add-on

1. **Let the add-on read extra config files.** This is set on the add-on, not on the MQTT integration page. Go to **Settings > Add-ons** (**Apps** in newer versions), open **Mosquitto broker** and switch to its **Configuration** tab. On the **Options** card, choose **Edit in YAML** from the three-dot menu, set the `customize` option as below, and save:

   ```yaml
   customize:
     active: true
     folder: mosquitto
   ```

   The add-on now also loads every `*.conf` file in `/share/mosquitto`.

2. **Create `/share/mosquitto/garagestack-bridge.conf`** with the bridge configuration above. Any add-on that can reach the `share` folder works:
   - **Studio Code Server**: the workspace only shows `/config`, but **File > Open Folder...** accepts `/share/`. Create the `mosquitto` folder and the file from there.
   - **Samba share**: open the `share` network folder from your computer.
   - **Terminal & SSH**: `mkdir -p /share/mosquitto`, then edit the file with `nano` or `vi`.

3. **Restart the Mosquitto broker add-on.** Its **Log** tab shows `Connecting bridge garagestack (<address>:1883)`, and on the GarageStack side the Mosquitto container logs `New bridge connected from ... as ha-bridge-garagestack`.

The car's retained discovery messages flow in as soon as the bridge connects, so the devices from step 3 appear without any further action. If they don't, reload the MQTT integration (**Settings > Devices & services > MQTT**, three-dot menu, **Reload**): Home Assistant then announces itself again and the gateway re-publishes discovery.

For any other Mosquitto broker, add the same lines to its configuration file and restart it.

---

## Things to know

- **Commands from Home Assistant skip GarageStack's command queue.** The MG cloud handles one command per car at a time, and each can take around 30 seconds. GarageStack queues its own commands for that reason, but Home Assistant's go straight to the gateway. Avoid automations that fire a command at the same moment as one from GarageStack, and in Home Assistant scripts wait for each command's state change before sending the next.
- **Anyone who can use your Home Assistant can now control the car.** Review who has access to it, and which dashboards show the lock and climate controls.
- **Keep the discovery prefix at `homeassistant`.** GarageStack reads the same discovery messages to detect whether your car is a HEV, PHEV or BEV, so don't disable discovery on the gateway or move it to another prefix.
- **Entities turn Unavailable when the gateway can't poll the car**, for example when the MG cloud is down or the session was taken over. That's the gateway's default behaviour and clears on the next successful poll.

---

## Troubleshooting

### Mosquitto exits with `HA_MQTT_PASSWORD is required when HA_MQTT_USERNAME is set`

Set both variables, or clear `HA_MQTT_USERNAME` to switch the Home Assistant login off. `HA_MQTT_USERNAME` must also differ from `MQTT_BROKER_USERNAME`.

### Home Assistant reports "not authorised" or "bad username or password"

Check both values, then restart the broker. A running broker keeps the credentials it started with.

### Home Assistant cannot connect at all

With Docker Compose, `MQTT_BIND_ADDRESS` defaults to `127.0.0.1`, so the broker is unreachable from other machines until you change it (step 2). Test from the Home Assistant machine with `mosquitto_sub -h <broker> -p 1883 -u homeassistant -P <password> -t 'homeassistant/#' -v`, which should print the discovery configs straight away.

### The bridge keeps reconnecting

The Mosquitto add-on's log repeats `Connecting bridge garagestack` with a growing backoff. If it also says `Connection Refused: not authorised`, the `remote_username` or `remote_password` in the bridge file doesn't match `HA_MQTT_USERNAME` / `HA_MQTT_PASSWORD`. If GarageStack's Mosquitto log shows no connection from Home Assistant at all, the connection never arrives: `address` points at the wrong machine, or `MQTT_BIND_ADDRESS` is still `127.0.0.1`.

### Two "SAIC Python MQTT Gateway" devices

The gateway creates one such device per MG account, named after `SAIC_USER`. A second one is a leftover discovery message from a gateway that once ran under a different account value, for example before you set up GarageStack. You can recognise it by an older **Firmware** version, every sensor **Unavailable**, and no activity. Check that the other device shows values before removing anything.

Leftovers are retained on a broker, and the Home Assistant login cannot remove discovery messages from GarageStack's broker, so deleting the device in Home Assistant alone brings it back on the next reconnect. First list the device IDs stored on GarageStack's broker (Docker Compose):

```bash
docker compose exec mosquitto sh -c 'mosquitto_sub -h 127.0.0.1 -u "$MQTT_BROKER_USERNAME" -P "$MQTT_BROKER_PASSWORD" -t "homeassistant/+/+/+/config" --retained-only -F "%t" -W 2 2>/dev/null | cut -d/ -f3 | grep "_gw$" | sort | uniq -c'
```

The stale device's ID also appears in its discovery topic under **MQTT Info** on its device page. Clear it, replacing `OLD_ID` with the ID without the `_gw` suffix:

```bash
docker compose exec mosquitto sh -c 'topics=$(mosquitto_sub -h 127.0.0.1 -u "$MQTT_BROKER_USERNAME" -P "$MQTT_BROKER_PASSWORD" -t "homeassistant/+/OLD_ID_gw/+/config" --retained-only -F "%t" -W 2 2>/dev/null); for t in $topics; do mosquitto_pub -h 127.0.0.1 -u "$MQTT_BROKER_USERNAME" -P "$MQTT_BROKER_PASSWORD" -t "$t" -r -n && echo "cleared $t"; done'
```

The removal reaches Home Assistant through the bridge, and the device disappears. If the ID wasn't listed on GarageStack's broker, the leftover lives only on Home Assistant's own broker: delete the device from its page in Home Assistant, which also clears it there.

### Connected, but no car appears

The gateway publishes discovery after its first successful login to the MG cloud, which can take a few minutes after a fresh start. Check the `saic-mqtt-gateway` log for login errors, then restart it once it is healthy.

---

## Environment variables

| Variable | Default | Description |
| --- | --- | --- |
| `HA_MQTT_USERNAME` | _(empty)_ | Broker login for Home Assistant. Empty means no Home Assistant login is created. |
| `HA_MQTT_PASSWORD` | _(empty)_ | Its password. Required when `HA_MQTT_USERNAME` is set. |
| `MQTT_BIND_ADDRESS` | `127.0.0.1` | Docker Compose only. Host interface the broker port is published on. |
| `MQTT_EXTERNAL_PORT` | `1883` | Docker Compose only. Host port the broker is published on. |
