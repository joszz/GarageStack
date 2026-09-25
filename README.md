# GarageStack

GarageStack is a free, open-source vehicle monitoring dashboard for **modern MG cars** -- vehicles manufactured by SAIC Motor (China) such as the MG4, MG5, ZS EV, HS PHEV, and similar models. It connects to the SAIC iSmart API (the same backend as the official MG iSmart app) and presents your car's live telemetry in a clean, self-hosted web app. The project is designed to work across HEV, PHEV, and BEV variants of the MG lineup - cards that are not relevant to your vehicle type are automatically hidden or adapted.

> **Note:** GarageStack only works with the current MG brand owned by SAIC Motor. It is **not** compatible with classic British-built MG cars (MGB, Midget, MGF, etc.) produced before SAIC's acquisition of the brand. If your car does not use the MG iSmart app, GarageStack will not work with it.

## Features

- **Live dashboard** -- Real-time vehicle telemetry displayed as configurable cards. Cards are automatically shown or hidden based on your vehicle type (HEV, PHEV, BEV) and can be reordered or toggled individually in the dashboard's edit mode.
- **Trip history** -- Browse past journeys on an interactive map with route playback and heatmap visualisation to identify frequently driven roads.
- **Energy statistics** -- Track daily energy consumption, efficiency (Wh/km on a plug-in car, L/100 km on a hybrid), fuel use, electric share, average driving speed, and more over a configurable time window.
- **Remote commands** -- Trigger climate pre-conditioning, lock or unlock the car, and activate the horn and lights remotely from the dashboard. Each command reports whether the car carried it out, and if it refused, the reason the MG servers gave.
- **Push notifications** -- Browser and in-app alerts for key events: engine started, low tyre pressure, low EV battery, car left unlocked, and doors or windows left open.
- **Homepage widget** -- A read-only API endpoint for the [gethomepage.dev](https://gethomepage.dev) Custom API widget, exposing key vehicle stats at a glance.
- **Home Assistant** -- Your car appears in Home Assistant automatically through MQTT discovery, sharing GarageStack's broker and MG session instead of needing a second integration. See [`HOME_ASSISTANT.md`](documentation/HOME_ASSISTANT.md).
- **Progressive Web App (PWA)** -- Installable on mobile or desktop for a native app-like experience, complete with a home screen icon and push notification support.
- **Charging stations** -- Overlay nearby EV charging stations on the map, sourced from the [Open Charge Map](https://openchargemap.org) database. Station data is cached in the database for 7 days; on page load the map immediately shows all stations within 100 km of your car that are already cached. Markers show operational status; clicking a marker displays the station name, operator, address, and available connector types with power ratings. Requires a free OCM API key (`OPENCHARGEMAP_API_KEY`). Unlike fuel stations and service areas, charging station tiles are loaded on demand as you browse the map and are not pre-populated by the background Worker.
- **Fuel stations** -- Overlay nearby petrol and diesel stations on the map (HEV and PHEV only; not shown for BEV). Sourced from OpenStreetMap via the Overpass API -- no API key required. POI data is cached in the database for 7 days and pre-populated by the Worker for a 100 km radius around the car's last known position so the overlay is instant on first view.
- **Motorway service areas** -- Overlay motorway service areas and rest stops on the map (all vehicle types). Same DB-backed cache and Worker pre-cache as fuel stations; useful for BEV drivers who often find fast chargers at service areas.
- **Speed cameras** -- Overlay the speed cameras mapped in OpenStreetMap (`highway=speed_camera`), with the limit each one enforces and the kind of camera it is where OSM records them. Same DB-backed cache and Worker pre-cache as fuel stations, off until you switch it on in the map's layer panel, and removable from the deployment with `SPEEDCAMERAS__ENABLED=false` -- see the note on jurisdictions in the Map overlays section.
- **Place names** -- Trips are listed by where they went ("Zwolle to Deventer") instead of by date alone, and the dashboard's location card names the street the car is parked in. Sourced from OpenStreetMap via [Nominatim](https://nominatim.openstreetmap.org) -- no API key required, answers are cached in the database for 90 days, and the whole feature can be switched off per browser under Settings > Map, or for the deployment with `GEOCODING__ENABLED=false`.
- **Snapped trip lines** -- A selected trip is drawn along the roads it was driven on rather than in straight lines between GPS fixes, which also gives a truer distance than the fixes alone. Matched against OpenStreetMap by [Valhalla](https://valhalla1.openstreetmap.de) -- no API key required, snapped trips are cached in the database for 30 days, and it can be switched off in the map's filter panel or for the deployment with `MAPMATCHING__ENABLED=false`.
- **Speed limits** -- The selected trip can be coloured against the limits signposted along it, green within and red above, with how far over it went and over how much of the trip a limit was known. Read from OpenStreetMap's `maxspeed` tags by the same match that snapped the trip, so it costs no extra request and needs no configuration.
- **Themed vector basemap** -- Every map is drawn from OpenStreetMap vector tiles by MapLibre GL, in a dark or light style that follows the interface theme and with labels in the interface language. Served by [OpenFreeMap](https://openfreemap.org) without an API key, point it at your own tile server if you prefer, and it falls back to raster tiles where WebGL is unavailable.
- **Single sign-on** -- Sign in through your own identity provider (Authentik, Authelia, Keycloak, Pocket ID, Google, and anything else speaking OpenID Connect), with optional auto-login and group or email based access restrictions. A built-in username/password login remains available for installs without a provider. See [`AUTHENTICATION.md`](documentation/AUTHENTICATION.md).
- **Multi-language support** -- Interface available in English and Dutch, with locale resolved from query string, cookie, or browser preference.
- **Self-hosted** -- Runs entirely on your own infrastructure via Docker (all-in-one container or Docker Compose). No cloud account or subscription required beyond the SAIC iSmart API.

### Dashboard cards

Cards are shown or hidden automatically based on vehicle type (HEV / PHEV / BEV). You can also reorder and toggle individual cards in the dashboard's edit mode.

| Card | Description | Vehicle types |
| ---- | ----------- | ------------- |
| Fuel Level | Tank level as a percentage | HEV, PHEV |
| Fuel Range | Estimated remaining range | HEV, PHEV |
| EV Battery | State of charge (%) | All |
| Electric Range | Distance the car estimates it can drive on the battery alone | PHEV, BEV |
| Charging | Charging indicator | PHEV, BEV |
| Odometer | Total distance driven | All |
| 12V Battery | Auxiliary battery voltage | All |
| Doors | Lock status and door states | All |
| Windows | Window and sunroof states | All |
| Sunroof | Sunroof open/closed | All (off by default) |
| Climate | Temperature, seat heating, defroster | All |
| HV Battery | State of charge, voltage, current, power (kWh too, with `HV_BATTERY_CAPACITY_KWH` set) | All |
| Find My Car | Horn + lights to locate the car | All |
| Lights | Main beam, low beam, sidelights | All |
| Daily Distance | Distance driven today | All |
| Daily Energy | Energy used today (kWh), or fuel burned today (L) on an HEV | All |
| Since Charge | Distance since last charge session | PHEV, BEV |
| Efficiency | Energy per km (Wh/km), or fuel consumption (L/100 km) on an HEV | All |
| Speed | Current vehicle speed | All |
| Top Speed | Highest speed recorded in the most recent completed trip | All |
| Active Trip | Distance covered in the current trip | All |
| Online Status | Whether the car is reachable via SAIC cloud | All |
| Charge Time | Estimated minutes remaining to charge limit | PHEV, BEV |
| Charging Session | OBC power, cable lock, charging type | PHEV, BEV |
| Battery Heating | Pre-heating status and schedule | PHEV, BEV |

### Statistics insights

The Statistics view shows insight cards and charts for a configurable period (7, 30, or 90 days). Cards are draggable and individually toggleable.

| Insight | Description |
| ------- | ----------- |
| Distance in period | Total distance across all trips in the selected window |
| Avg trip length | Average distance per trip |
| Remote preconditioning | Percentage of snapshots with remote climate active |
| Peak drive time | Hour of day with the most trip starts |
| 12V trend | Change in average 12V battery voltage over the period |
| Parking locations | Number of distinct parking spots (rounded GPS) |
| Electric share today | Estimated share of today's driving on electric power (PHEV only) |
| Avg speed | Average moving speed across all GPS points in the period, excluding stopped moments |

## Screenshots

| Desktop          | Mobile         |
| ---------------- | -------------- |
| ![Desktop][desk] | ![Mobile][mob] |

[desk]: frontend/public/screenshot-desktop-home.webp "Desktop dashboard"
[mob]: frontend/public/screenshot-mobile-home.webp "Mobile dashboard"

---

## MG iSmart account and session limits

> **Important:** The MG iSmart API only allows one active session per account at a time. Logging in anywhere else with the same credentials -- including the official MG app -- will immediately invalidate GarageStack's session, causing telemetry to stop until GarageStack reconnects.

GarageStack needs the vehicle **owner account**: shared or secondary accounts lack the write permissions required to register alarm switches and will fail with error 1100003. So instead of moving GarageStack off the owner account, set up a secondary account for the official MG app to use, and keep the owner account free for GarageStack:

1. Open the MG app and go to **Settings > Account management > Add secondary account** (exact wording varies by region and app version).
2. Invite a second email address and accept the invite on that account.
3. Grant the secondary account access to your vehicle.
4. Sign in to the official MG app with the secondary account, and use the owner account's credentials for `SAIC_USER` / `SAIC_PASSWORD` in GarageStack.

This way the official app runs independently on the secondary account and GarageStack keeps its own session on the owner account.

The same applies to Home Assistant: rather than adding an MG integration there, connect Home Assistant to GarageStack's MQTT broker so both share one session. See [`HOME_ASSISTANT.md`](documentation/HOME_ASSISTANT.md).

---

## Prerequisites

Before installing, make sure the following are available on your host:

- **Docker** 20.10 or later (or Podman with Docker Compose compatibility)
- **Docker Compose v2** (bundled with Docker Desktop; on Linux install the `docker-compose-plugin` package) -- required for Option B only
- An **MG iSmart account** with your vehicle already linked in the official app
- Outbound internet access on the host so the container can reach the SAIC API and (optionally) Overpass / Open Charge Map

---

## Installation

Choose the method that fits your environment.

---

### Option A: All-in-one container (Unraid / homelab)

A single Docker image that bundles every service -- nginx, the .NET API + worker, PostgreSQL, Mosquitto, and the SAIC gateway. No Compose file or external database needed. Ideal for Unraid and similar NAS environments where running multiple containers is inconvenient.

#### Quick start

```bash
docker run -d \
  --name garagestack \
  -p 8080:80 \
  -v ./garagestack-data:/data \
  -e SAIC_USER=your@email.com \
  -e SAIC_PASSWORD=yourpassword \
  -e SAIC_REGION=eu \
  -e CORS_ORIGIN=http://192.168.1.100:8080 \
  -e AUTH_COOKIE_SECURE=false \
  ghcr.io/joszz/garagestack:latest
```

> `POSTGRES_PASSWORD` is omitted -- a strong random password is auto-generated on first start and saved to `/data/.postgres_password`. Pass `-e POSTGRES_PASSWORD=yourpassword` explicitly if you need a known value (e.g. to connect with an external DB tool).
> **HTTPS proxy:** omit `-e AUTH_COOKIE_SECURE=false` (or set it to `true`) when the container sits behind a TLS-terminating reverse proxy.
> **Logging in:** without further configuration the web login uses your `SAIC_USER` / `SAIC_PASSWORD`. Set `AUTH_USERNAME` / `AUTH_PASSWORD` for separate credentials, or point GarageStack at your identity provider -- see [`AUTHENTICATION.md`](documentation/AUTHENTICATION.md).

**Unraid:** import `unraid/garagestack.xml` from Community Apps and fill in the variables in the template UI.

See [`docker/all-in-one/README.md`](docker/all-in-one/README.md) for the full variable reference, volume layout, and Unraid setup steps.

---

### Option B: Docker Compose

Separate containers for each service. More flexible -- you can swap in your own PostgreSQL or MQTT broker, and containers update independently.

#### 1. Clone the repository

```bash
git clone https://github.com/joszz/garagestack.git
cd garagestack
```

**2. Create your `.env` file**

```bash
cp .env.example .env
```

Then open `.env` and fill in at minimum:

| Variable | Description |
| ---------- | ----------- |
| `SAIC_USER` | MG iSmart account email |
| `SAIC_PASSWORD` | MG iSmart account password |
| `SAIC_REGION` | Region the car is registered in: `eu` (default), `au`, or `tr` -- automatically mapped to the right API endpoint |
| `SAIC_REST_URI` | Optional, only needed for a region not listed above -- set directly to your gateway's endpoint |
| `CORS_ORIGIN` | The URL you open in your browser, e.g. `http://192.168.1.100:8080` |
| `POSTGRES_PASSWORD` | Generate with `openssl rand -hex 32`. Using an external Postgres server instead of the bundled one? Set this to match its existing password. |
| `MQTT_BROKER_PASSWORD` | Generate with `openssl rand -hex 32`. Mosquitto always runs bundled, so this is required either way. |

`docker compose up` refuses to start until both `POSTGRES_PASSWORD` and `MQTT_BROKER_PASSWORD` are set.

`VAPID_PUBLIC_KEY` / `VAPID_PRIVATE_KEY` are optional; leave them empty to disable push notifications.

`HA_MQTT_USERNAME` / `HA_MQTT_PASSWORD` are optional and create a restricted broker login for Home Assistant. See [`HOME_ASSISTANT.md`](documentation/HOME_ASSISTANT.md).

Sign-in is configured separately: the built-in login reuses `SAIC_USER` / `SAIC_PASSWORD` unless you set `AUTH_USERNAME` / `AUTH_PASSWORD`, and setting `OIDC_AUTHORITY` switches GarageStack over to your identity provider. See [`AUTHENTICATION.md`](documentation/AUTHENTICATION.md).

`TYRE_PRESSURE_LOW_BAR` / `TYRE_PRESSURE_GOOD_BAR` / `TYRE_PRESSURE_HIGH_BAR` are optional and default to `2.2` / `2.6` / `3.2` bar; override them to match your vehicle's placarded tyre pressure (see [Push notifications](#push-notifications) below).

`HV_BATTERY_CAPACITY_KWH` is optional and tells GarageStack how big the traction battery really is. The MQTT gateway does not read this off the pack, it scales the BMS percentage by an EV-sized default, so the kWh it reports are right for a BEV or PHEV and far too large for a plain hybrid (an MG HS Hybrid+ carries 1.83 kWh and is reported as 72.5). Left unset, a plug-in car keeps the gateway's figure and a hybrid shows state of charge as a percentage only.

`RATE_LIMIT_GLOBAL_PER_MINUTE` is optional and defaults to `120` requests per minute per client IP. Raise it when several people reach GarageStack through one public address, or when something polls the API frequently; the tighter limits protecting login and the widget endpoint are unaffected.

#### 3. Start the stack

With the bundled PostgreSQL container:

```bash
docker compose --profile bundled-postgres up -d
```

Using your own existing PostgreSQL server (set `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_USER`, `POSTGRES_DB`, `POSTGRES_PASSWORD` in `.env` to match):

```bash
docker compose up -d
```

The frontend is served on port `8080` by default (configurable via `FRONTEND_PORT` in `.env`).

---

## Updating

Pull the latest image and restart. Database migrations run automatically on startup.

**Docker Compose:**

```bash
docker compose pull
docker compose up -d
```

**All-in-one container:**

```bash
docker pull ghcr.io/joszz/garagestack:latest
docker stop garagestack && docker rm garagestack
# Re-run your original docker run command
```

---

## Backup and restore

The only data that matters for disaster recovery is your PostgreSQL database (all vehicle telemetry/trip history) and the DataProtection keys used to encrypt login cookies -- losing the keys just logs everyone out, it doesn't lose any vehicle data.

**Docker Compose, bundled Postgres:** back up the database with `pg_dump` via the `postgres` service:

```bash
docker compose exec postgres pg_dump -U garagestack garagestack > garagestack-backup.sql
```

Restore into a fresh database:

```bash
cat garagestack-backup.sql | docker compose exec -T postgres psql -U garagestack garagestack
```

(Replace `garagestack`/`garagestack` with your `POSTGRES_USER`/`POSTGRES_DB` if you customised them.)

**Docker Compose, external Postgres:** the `postgres` service above only exists when started with `--profile bundled-postgres`, so `docker compose exec postgres ...` won't work here -- back up directly against your own server instead, using the same `POSTGRES_HOST`/`POSTGRES_PORT`/`POSTGRES_USER`/`POSTGRES_DB` values you set in `.env`:

```bash
pg_dump -h <POSTGRES_HOST> -p <POSTGRES_PORT> -U <POSTGRES_USER> <POSTGRES_DB> > garagestack-backup.sql
```

Restore the same way, with `psql` in place of `pg_dump`:

```bash
psql -h <POSTGRES_HOST> -p <POSTGRES_PORT> -U <POSTGRES_USER> <POSTGRES_DB> < garagestack-backup.sql
```

Either way, the DataProtection keys live in the `api_dataprotection` named volume -- back it up with the API stopped so nothing is mid-write:

```bash
docker compose stop api
docker run --rm -v garagestack_api_dataprotection:/keys -v "$(pwd)":/backup alpine tar czf /backup/dataprotection-backup.tar.gz -C /keys .
docker compose start api
```

(Volume names are prefixed with the Compose project name -- run `docker volume ls` to confirm yours if it's not `garagestack`.) The `mosquitto_data`, `worker_logs`, and `api_logs` volumes hold disposable/regeneratable data and don't need backing up.

**All-in-one container:** everything lives under the single `/data` bind mount (`garagestack-data/` by default). Stop the container first so nothing is mid-write, then copy the whole directory:

```bash
docker stop garagestack
cp -r garagestack-data garagestack-data-backup
docker start garagestack
```

To restore, stop the container, replace `garagestack-data` with the backup, and start it again.

---

## Push notifications

GarageStack checks your vehicle's state every 5 minutes and sends both a browser push notification and an in-app notification (bell icon) when any of the following conditions are detected. Each alert has a 1-hour cooldown per vehicle to avoid repeated notifications.

| Alert | Condition |
| ------- | --------- |
| Engine started | Engine transitions from off to running |
| Low tyre pressure | Any tyre below `TYRE_PRESSURE_LOW_BAR` (default 2.2 bar) |
| High tyre pressure | Any tyre above `TYRE_PRESSURE_HIGH_BAR` (default 3.2 bar) |
| Low EV battery | EV state-of-charge below 20 % (plug-in vehicles) |
| Car left unlocked | `doors/locked = false` while engine is off |
| Door left open | Any door, boot, or bonnet open while engine is off |
| Window left open | Any window open while engine is off |
| Charging complete | Charging stops while the cable is still connected (plug-in vehicles) |
| Maintenance due | A maintenance item reaches 90 % of its interval, or passes it (checked every 6 hours, 7-day cooldown per item) |

Push notifications require VAPID keys to be configured (`VAPID_PUBLIC_KEY` / `VAPID_PRIVATE_KEY`). Without them, alerts still appear in the in-app notification panel. The "engine started" alert is also triggered in real time when the event arrives over MQTT, independently of the 5-minute polling cycle.

Settings has a per-type checklist for these alerts, which offers only the types the car's drivetrain can produce: the two plug-in alerts (low EV battery, charging complete) are left out for a plain hybrid. Deselecting every type offered unsubscribes the browser from push entirely; selecting one again resubscribes.

Notification texts are written by the background worker, which has no browser to take a language from, so their language is a deployment setting: `NOTIFICATION_LANGUAGE=en` (default) or `nl`. The web UI's own language toggle does not affect them.

The tyre pressure thresholds (`TYRE_PRESSURE_LOW_BAR` / `TYRE_PRESSURE_GOOD_BAR` / `TYRE_PRESSURE_HIGH_BAR`) also drive the colour-coded dots on the dashboard's vehicle diagram and the in-browser low/high pressure alert -- set them once in your `.env` (or container environment) to match your vehicle's placarded pressure instead of the app's generic defaults.

To generate a VAPID key pair (requires Node.js):

```bash
npx web-push generate-vapid-keys
```

No Node.js installed? Use a temporary Docker container instead:

```bash
docker run --rm node:lts-alpine npx --yes web-push generate-vapid-keys
```

Copy the public key to `VAPID_PUBLIC_KEY` and the private key to `VAPID_PRIVATE_KEY` in your `.env` file (or as container environment variables). Keep the private key secret -- regenerating it invalidates all existing push subscriptions, requiring users to re-enable notifications in the browser.

Note: "keys left in the car" is not currently supported because the SAIC MQTT gateway does not expose a key-in-vehicle sensor.

---

## Homepage dashboard widget

GarageStack exposes a dedicated read-only endpoint for the [gethomepage.dev](https://gethomepage.dev) [Custom API widget](https://gethomepage.dev/widgets/services/customapi/). No fork or custom widget code is required.

### 1. Generate an API key

```bash
openssl rand -base64 32
```

Set `WIDGET_API_KEY` to the generated value in your `.env` file (Docker Compose) or as a container environment variable (all-in-one / Unraid). Leave it empty to keep the endpoint disabled.

### 2. Find your VIN

Log in to GarageStack, open the browser developer tools (Network tab) and reload: the VIN is the `vin` field in the `/api/vehicles` response. It is also the 17-character segment in the MQTT topics the gateway logs, `saic/<account>/vehicles/<VIN>/...`.

### 3. Configure Homepage

Add the following block to your Homepage `services.yaml`, replacing `YOUR_GARAGESTACK_URL`, `YOUR_VIN`, and `YOUR_WIDGET_API_KEY`:

```yaml
- GarageStack:
    href: https://YOUR_GARAGESTACK_URL
    description: MG Vehicle Status
    widget:
      type: customapi
      url: https://YOUR_GARAGESTACK_URL/api/widget/YOUR_VIN/status
      headers:
        X-Widget-Key: "YOUR_WIDGET_API_KEY"
      mappings:
        - field: evSocPercent
          label: Battery
          format: percent
        - field: isCharging
          label: Charging
          format: text
        - field: exteriorTemperature
          label: Ext. Temp
          format: float
          suffix: "°C"
        - field: isLocked
          label: Locked
          format: text
```

### Available fields

The endpoint returns a flat JSON object. Numeric fields are `null` when the vehicle has not reported that value yet. String state fields are also `null` when unreported, except `anyDoorOpen` and `anyWindowOpen` which are always present. String values are localized: the language is resolved from the request in this order: query string, cookie, `Accept-Language` header, falling back to `en`. Supported languages are `en` and `nl`. To pin a language regardless of the Homepage container's locale, append `?culture=nl&ui-culture=nl` (or `en`) to the widget URL.

| Field | Type | Description |
| ------- | ------ | ----------- |
| `recordedAt` | string (ISO 8601) | Timestamp of the most recent telemetry |
| `fuelLevelPercent` | number | Fuel tank level (%) |
| `fuelRangeKm` | number | Estimated fuel range (km) |
| `evSocPercent` | number | EV / HV battery state of charge (%) |
| `electricRangeKm` | number | Distance the car estimates it can drive on the battery alone (km), on a BEV or PHEV. Never reported as 0, so an empty battery keeps its last value |
| `isCharging` | string | Charging state: `"Charging"` or `"Not charging"` |
| `chargerConnected` | string | Charger connection state: `"Plugged in"` or `"Unplugged"` |
| `mileageSinceLastCharge` | number | Distance driven since last full charge (km) |
| `hvSocKwh` | number | HV battery energy (kWh), as the gateway reports it |
| `hvTotalCapacityKwh` | number | HV battery total capacity (kWh), as the gateway reports it |
| `hvVoltage` | number | HV system voltage (V) |
| `hvCurrent` | number | HV system current (A) |
| `hvPower` | number | HV system power (W) |
| `odometerKm` | number | Total odometer reading (km) |
| `mileageOfTheDayKm` | number | Distance driven today (km) |
| `powerUsageOfDayKwh` | number | Energy used today (kWh). On a plain hybrid this counter holds the trip computer's fuel total in hundredths of a litre instead, so divide by 100 for litres |
| `electricSharePercent` | number | % of today's distance driven on electric power (PHEV) |
| `isLocked` | string | Lock state: `"Locked"` or `"Unlocked"` |
| `engineRunning` | string | Engine state: `"Engine on"` or `"Engine off"` |
| `climateOn` | string | Remote climate state: `"On"` or `"Off"` |
| `driverDoorOpen` | string | Driver door state: `"Open"` or `"Closed"` |
| `passengerDoorOpen` | string | Passenger door state: `"Open"` or `"Closed"` |
| `rearLeftDoorOpen` | string | Rear left door state: `"Open"` or `"Closed"` |
| `rearRightDoorOpen` | string | Rear right door state: `"Open"` or `"Closed"` |
| `trunkOpen` | string | Boot / trunk state: `"Open"` or `"Closed"` |
| `bonnetOpen` | string | Bonnet / hood state: `"Open"` or `"Closed"` |
| `anyDoorOpen` | string | `"Open"` if any door, boot, or bonnet is open, otherwise `"Closed"` (never null) |
| `driverWindowOpen` | string | Driver window state: `"Open"` or `"Closed"` |
| `passengerWindowOpen` | string | Passenger window state: `"Open"` or `"Closed"` |
| `rearLeftWindowOpen` | string | Rear left window state: `"Open"` or `"Closed"` |
| `rearRightWindowOpen` | string | Rear right window state: `"Open"` or `"Closed"` |
| `sunRoofOpen` | string | Sunroof state: `"Open"` or `"Closed"` |
| `anyWindowOpen` | string | `"Open"` if any window or sunroof is open, otherwise `"Closed"` (never null) |
| `batteryVoltage` | number | 12V auxiliary battery voltage (V) |
| `interiorTemperature` | number | Interior temperature (°C) |
| `exteriorTemperature` | number | Exterior temperature (°C) |
| `tyrePressureFrontLeft` | number | Front-left tyre pressure (bar) |
| `tyrePressureFrontRight` | number | Front-right tyre pressure (bar) |
| `tyrePressureRearLeft` | number | Rear-left tyre pressure (bar) |
| `tyrePressureRearRight` | number | Rear-right tyre pressure (bar) |
| `lightsMainBeam` | string | Main beam headlights state: `"On"` or `"Off"` |
| `lightsDippedBeam` | string | Dipped beam headlights state: `"On"` or `"Off"` |
| `lightsSide` | string | Side / parking lights state: `"On"` or `"Off"` |
| `speedKmh` | number | Current vehicle speed (km/h) |
| `currentJourneyDistanceKm` | number | Distance driven in the current trip (km) |
| `isAvailable` | string | Cloud reachability: `"Online"` or `"Offline"` |
| `lastVehicleStateAt` | string (ISO 8601) | Timestamp the car last pushed state to SAIC cloud |
| `lastChargeStateAt` | string (ISO 8601) | Timestamp the car last pushed charge state to SAIC cloud |
| `remainingChargingTime` | number | Estimated minutes remaining to reach charge limit |
| `chargingType` | string | Charging type as reported by the gateway (e.g. `"AC"`, `"DC"`) |
| `chargingCableLock` | string | Cable lock state: `"Locked"` or `"Unlocked"` |
| `obcPowerSinglePhase` | number | Onboard charger single-phase AC power (kW) |
| `obcPowerThreePhase` | number | Onboard charger three-phase AC power (kW) |
| `batteryHeating` | string | Battery pre-heating state: `"On"` or `"Off"` |
| `batteryHeatingScheduleMode` | string | Battery heating schedule mode (e.g. `"off"`) |
| `batteryHeatingScheduleStartTime` | string | Battery heating schedule start time (HH:MM) |
| `elevation` | number | Vehicle elevation above sea level (m) |
| `bmsChargeStatus` | string | BMS charge status string (e.g. `"UNPLUGGED"`, `"CHARGING"`) |
| `lastChargeEndingPower` | number | State of charge (%) when the last charge session ended |
| `chargingLastEndAt` | string (ISO 8601) | Timestamp the last charge session ended |
| `chargingScheduleMode` | string | Scheduled charging mode (e.g. `"DISABLED"`, `"UNTIL_CONFIGURED_TIME"`) |
| `chargingScheduleStartTime` | string | Scheduled charge start time (HH:MM) |
| `chargingScheduleEndTime` | string | Scheduled charge end time (HH:MM) |
| `onboardChargerPlugStatus` | number | Onboard charger plug presence status (raw integer) |
| `offboardChargerPlugStatus` | number | Offboard (DC) charger plug presence status (raw integer) |

---

## Map overlays

The map view supports three POI overlay layers. All data is cached in the database and served instantly on subsequent visits. Open the **Filters** panel (sliders icon, top-right of the map) to toggle each layer and adjust filters.

### Basemap

The map underneath every overlay is drawn from OpenStreetMap vector tiles by [MapLibre GL](https://maplibre.org), served by [OpenFreeMap](https://openfreemap.org) -- no API key required.

- The basemap follows the interface theme: a dark style in the dark theme, a light one in the light theme, switching without a page reload.
- Labels follow the interface language, so the same map reads "Duitsland" in Dutch and "Germany" in English. Names come from OpenStreetMap's own `name:<language>` tags and fall back to the local name where no translation exists.
- The renderer (about 1.5 MB) is downloaded only when a page with a map opens, and is deliberately kept out of the service worker's precache so an install does not pay for it up front.
- A browser without WebGL, or a tile server that cannot be reached, falls back to the classic OpenStreetMap raster tiles automatically.
- To serve the basemap yourself (your own OpenFreeMap or any MapLibre style), build the frontend image with `--build-arg MAP_STYLE_DARK=...` and `--build-arg MAP_STYLE_LIGHT=...`, and add that host to `connect-src` and `img-src` in `frontend/nginx-security-headers.conf` -- the Content-Security-Policy only allows the hosts listed there.

### Charging stations

Requires a free [Open Charge Map](https://openchargemap.org/site/develop) API key (`OPENCHARGEMAP_API_KEY`).

- Markers show operational status at a glance.
- Clicking a marker shows the station name, operator, address, and available connectors with power ratings.
- **Power filter** -- a dual-handle slider lets you restrict results to a specific kW range (e.g. 50-150 kW for fast DC only). The filter is applied client-side from the local cache; no new API call is made when you move the slider. Set the upper handle to the maximum (350+) to remove the upper limit.
- Tile data is loaded on demand as you browse the map and cached for 7 days. The Worker does not pre-populate charging tiles.
- Available for BEV and PHEV vehicles only; hidden for HEV.

### Fuel stations (HEV and PHEV only)

Sourced from OpenStreetMap via the free [Overpass API](https://overpass-api.de) -- no API key required.

> **Performance note:** Fetching data from the Overpass API for uncached map tiles can be slow -- a single tile request may take anywhere from a few seconds to over 30 seconds depending on Overpass server load and the density of POIs in the area. The background Worker pre-populates a 100 km radius around your car's position on startup and every 6 hours, so that area loads instantly. Outside that radius, each newly-visible tile triggers an on-demand Overpass fetch; expect a visible delay until it is cached. Subsequent visits load from the database with no external call.

- Shows petrol and diesel stations from OSM data. Accuracy and completeness depend on OSM coverage in your area.
- **Brand filter** -- select one or more brands (e.g. BP, Shell, Total) from the Filters panel. Only stations with a matching `brand` or `operator` OSM tag are shown; untagged stations are hidden when any filter is active. The filter is applied client-side with no additional API call.
- The Worker pre-populates a 100 km radius around your car's last known position every 6 hours, so the layer loads instantly on first view without hitting Overpass.
- If Overpass returns a 429 rate-limit response, the client backs off and retries automatically; existing cached data is shown in the meantime.
- Tile data is cached for 7 days. Zooming or panning to a new area triggers on-demand fetching for uncached tiles.
- Hidden for BEV vehicles (petrol stations are not relevant).

### Motorway service areas

Sourced from OpenStreetMap (`highway=services`) via the Overpass API -- no API key required.

- Shows motorway service areas and rest stops.
- Available for all vehicle types; useful for BEV drivers because many service areas have fast-charger banks.
- Same DB-backed cache and Worker pre-population as fuel stations. The same Overpass slowness applies for uncached tiles outside the pre-populated radius -- see the note in the Fuel stations section above.

### Speed cameras

Sourced from OpenStreetMap (`highway=speed_camera`) via the Overpass API -- no API key required.

- Shows the cameras OSM holds a position for, all vehicle types. A marker's popup names the limit the camera enforces (`maxspeed`), the kind of camera (fixed, mobile site, average speed check, red light) and its operator, as far as the node is tagged for them. Coverage and accuracy are whatever OSM's mappers have entered.
- Off by default. **The map's layer panel > Speed cameras** switches it on per browser.
- Positions on a map, not warnings: GarageStack is a dashboard of the car's state, and nothing here alerts you to a camera ahead while driving.
- Several countries restrict pointing a driver at camera positions, France and Germany among them. Set `SPEEDCAMERAS__ENABLED=false` to remove the layer from the deployment: the toggle then disappears from the layer panel, the endpoint serves nothing, and the Worker fetches no camera data either.
- Same DB-backed cache and Worker pre-population as fuel stations. The same Overpass slowness applies for uncached tiles outside the pre-populated radius -- see the note in the Fuel stations section above.

### Place names (reverse geocoding)

Sourced from OpenStreetMap via [Nominatim](https://nominatim.openstreetmap.org) -- no API key required.

- The trip list leads with the route ("Zwolle to Deventer", or one city for a round trip) and moves the date into the line below it. Until the names arrive, the row shows a placeholder; if geocoding is off or unavailable, it keeps showing the date, exactly as before.
- The dashboard's location card and the car's map popup show the street and city the car is standing in.
- Answers are cached in PostgreSQL for 90 days, keyed by a coordinate grid and by language, so the same parking spot is looked up once. "Nothing mapped here" is cached for a day, since OSM coverage grows.
- The public Nominatim instance allows about one request per second, so a request resolves at most three new coordinates and the browser asks again for the rest; a cold trip list fills in over a few seconds and is instant from then on.
- Names follow the interface language, so switching between English and Dutch looks the places up again in that language.
- **Settings > Map > Show place names** turns the whole thing off per browser (it is on by default). Off, trips are listed by date, the location card shows no address, and no coordinates leave the browser.
- Set `GEOCODING__ENABLED=false` to switch it off for the whole deployment instead, or point `GEOCODING__BASEURL` at your own Nominatim instance.

### Snapped trip lines (map matching)

Telemetry arrives as GPS fixes tens of seconds apart, so a trip drawn straight from them cuts every corner and runs through buildings. Selecting a trip sends its fixes to [Valhalla](https://valhalla1.openstreetmap.de) -- the router behind OpenStreetMap's own directions -- which answers with the roads underneath them. No API key required.

- Only the selected trip is snapped. The raw line is drawn immediately and replaced when the answer lands, so the map never waits.
- The pill at the top of the map shows the snapped distance, which is the better figure: the straight-line distance through the fixes is always short by the corners it cut.
- The speed overlay follows the snapped line too, so its colours sit on the route that was driven.
- A trip with a hole in its telemetry (the gateway stopped publishing for a while) is matched in parts and stitched back together, with the hole left as the straight jump it always was. Valhalla refuses a trace containing a jump wider than 10 km, which is exactly what such a hole looks like.
- A match whose length no longer resembles the fixes it came from is discarded and the raw line kept: with sparse fixes a router can return a plausible route that is not the one taken.
- Snapped trips are cached in PostgreSQL for 30 days, keyed by a hash of the fixes, so a trip is matched once however often it is selected. "These fixes snap to nothing" is cached for a day.
- **The map's filter panel > Snap to roads** turns it off per browser (it is on by default), and trips are then drawn from their raw fixes as before.
- Set `MAPMATCHING__ENABLED=false` to switch it off for the whole deployment instead, or point `MAPMATCHING__BASEURL` at your own Valhalla instance.

### Speed limits

The matcher also answers with the limit on each stretch of road it snapped the trip onto, from OpenStreetMap's `maxspeed` tags. **The map's filter panel > Speed limits** colours the selected trip against them: green within the limit, red above it, grey where OSM holds no limit. The legend adds how far over the limit the trip went, and over how much of its distance a limit was known at all. It needs no extra configuration, and no request beyond the one that snapped the trip.

Two things it cannot know, which the legend and its tooltip say rather than hide:

- **Not every road has a limit in OSM.** The share of the trip where one is known is shown, and a trip over nothing but untagged roads keeps its ordinary colour instead of drawing entirely grey.
- **Conditional limits are not in the data.** A Dutch motorway signed 100 by day and 130 at night is tagged 100, so a night drive there reads as over the limit. The same goes for variable-limit sections.

A reading counts as over the limit only past 5 km/h above it: speedometers read high by a few percent by design, and a fix is a spot sample rather than an average over the stretch it covers.

### Caching architecture

All four POI types share the same tile-based PostgreSQL cache:

- The map is divided into a 0.5 deg x 0.5 deg grid (roughly 55 x 40 km at European latitudes).
- Each tile is fetched once and stored for 7 days; subsequent requests for the same area are served from the database with no external API call.
- The background Worker pre-populates tiles around your car on startup and every 6 hours (the OpenStreetMap layers only: fuel, service areas and speed cameras, the last of these unless the deployment has switched them off).
- The `MaxOnDemandTiles` cap (1 per API request) prevents Overpass rate-limiting when many uncached tiles are requested at once; the frontend chains requests automatically with back-off when more tiles remain.

---

## Security defaults

- API routes require login.
- Sign-in goes through your own identity provider when `OIDC_AUTHORITY` is configured, which also disables the built-in password login unless you keep it on with `AUTH_PASSWORD_LOGIN_ENABLED=true`. Without a provider, the built-in login reuses the configured MG account credentials unless `AUTH_USERNAME`/`AUTH_PASSWORD` are set. Full reference: [`AUTHENTICATION.md`](documentation/AUTHENTICATION.md).
- Sessions are encrypted, HTTP-only, `SameSite=Strict` cookies. Logout revokes the session server-side, not just the client-side cookie.
- With an identity provider, restrict who may sign in -- at the provider itself or with `OIDC_ALLOWED_GROUPS` / `OIDC_ALLOWED_EMAILS`. Without a restriction, every account the provider accepts can sign in and control the car.
- Login endpoints are rate-limited per IP address on top of the global limit.
- MQTT now requires credentials and ACLs, and broker exposure defaults to localhost-only in Docker Compose.
- The optional Home Assistant broker login can only use the car's `saic/#` topics and read Home Assistant discovery, so it cannot publish fake discovery configs or reach anything else on the broker.

---

## GitHub Actions / CI

The Docker build workflow requires two repository secrets to avoid Docker Hub anonymous pull rate limits (GitHub runners share IPs and exhaust the limit quickly):

- **`DOCKERHUB_USERNAME`** - Your Docker Hub username
- **`DOCKERHUB_TOKEN`** - A Docker Hub access token (hub.docker.com > Account Settings > Security > New Access Token)

Add them under **Settings > Secrets and variables > Actions** in your fork. A free Docker Hub account is sufficient.

---

> **Development note:** This project was built with AI-assisted development (Claude Code). All code was reviewed, directed, and validated by a human developer throughout - AI acted as a coding assistant, not an autonomous agent.
