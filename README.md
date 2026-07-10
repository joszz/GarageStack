# GarageStack

GarageStack is a free, open-source vehicle monitoring dashboard for **modern MG cars** -- vehicles manufactured by SAIC Motor (China) such as the MG4, MG5, ZS EV, HS PHEV, and similar models. It connects to the SAIC iSmart API (the same backend as the official MG iSmart app) and presents your car's live telemetry in a clean, self-hosted web app. The project is designed to work across HEV, PHEV, and BEV variants of the MG lineup - cards that are not relevant to your vehicle type are automatically hidden or adapted.

> **Note:** GarageStack only works with the current MG brand owned by SAIC Motor. It is **not** compatible with classic British-built MG cars (MGB, Midget, MGF, etc.) produced before SAIC's acquisition of the brand. If your car does not use the MG iSmart app, GarageStack will not work with it.

## Features

- **Live dashboard** -- Real-time vehicle telemetry displayed as configurable cards. Cards are automatically shown or hidden based on your vehicle type (HEV, PHEV, BEV) and can be reordered or toggled individually in the dashboard's edit mode.
- **Trip history** -- Browse past journeys on an interactive map with route playback and heatmap visualisation to identify frequently driven roads.
- **Energy statistics** -- Track daily energy consumption, efficiency (Wh/km), fuel use, electric share, average driving speed, and more over a configurable time window.
- **Remote commands** -- Trigger climate pre-conditioning, lock or unlock the car, and activate the horn and lights remotely from the dashboard.
- **Push notifications** -- Browser and in-app alerts for key events: engine started, low tyre pressure, low EV battery, car left unlocked, and doors or windows left open.
- **Homepage widget** -- A read-only API endpoint for the [gethomepage.dev](https://gethomepage.dev) Custom API widget, exposing key vehicle stats at a glance.
- **Progressive Web App (PWA)** -- Installable on mobile or desktop for a native app-like experience, complete with a home screen icon and push notification support.
- **Charging stations** -- Overlay nearby EV charging stations on the map, sourced from the [Open Charge Map](https://openchargemap.org) database. Station data is cached in the database for 7 days; on page load the map immediately shows all stations within 100 km of your car that are already cached. Markers show operational status; clicking a marker displays the station name, operator, address, and available connector types with power ratings. Requires a free OCM API key (`OPENCHARGEMAP_API_KEY`). Unlike fuel stations and service areas, charging station tiles are loaded on demand as you browse the map and are not pre-populated by the background Worker.
- **Fuel stations** -- Overlay nearby petrol and diesel stations on the map (HEV and PHEV only; not shown for BEV). Sourced from OpenStreetMap via the Overpass API -- no API key required. POI data is cached in the database for 7 days and pre-populated by the Worker for a 100 km radius around the car's last known position so the overlay is instant on first view.
- **Motorway service areas** -- Overlay motorway service areas and rest stops on the map (all vehicle types). Same DB-backed cache and Worker pre-cache as fuel stations; useful for BEV drivers who often find fast chargers at service areas.
- **Multi-language support** -- Interface available in English and Dutch, with locale resolved from query string, cookie, or browser preference.
- **Self-hosted** -- Runs entirely on your own infrastructure via Docker (all-in-one container or Docker Compose). No cloud account or subscription required beyond the SAIC iSmart API.

### Dashboard cards

Cards are shown or hidden automatically based on vehicle type (HEV / PHEV / BEV). You can also reorder and toggle individual cards in the dashboard's edit mode.

| Card | Description | Vehicle types |
| ---- | ----------- | ------------- |
| Fuel Level | Tank level as a percentage | HEV, PHEV |
| Fuel Range | Estimated remaining range | HEV, PHEV |
| EV Battery | State of charge (%) | All |
| Charging | Charging indicator | PHEV, BEV |
| Odometer | Total distance driven | All |
| 12V Battery | Auxiliary battery voltage | All |
| Doors | Lock status and door states | All |
| Windows | Window and sunroof states | All |
| Sunroof | Sunroof open/closed | All (off by default) |
| Climate | Temperature, seat heating, defroster | All |
| HV Battery | kWh, voltage, current, power | All |
| Find My Car | Horn + lights to locate the car | All |
| Lights | Main beam, low beam, sidelights | All |
| Daily Distance | Distance driven today | All |
| Daily Energy | Energy used today (Wh) | All |
| Since Charge | Distance since last charge session | PHEV, BEV |
| Efficiency | Energy per km (Wh/km) | All |
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
  -e JWT_SECRET="$(openssl rand -base64 32)" \
  -e CORS_ORIGIN=http://192.168.1.100:8080 \
  -e AUTH_COOKIE_SECURE=false \
  ghcr.io/joszz/garagestack:latest
```

> `POSTGRES_PASSWORD` is omitted -- a strong random password is auto-generated on first start and saved to `/data/.postgres_password`. Pass `-e POSTGRES_PASSWORD=yourpassword` explicitly if you need a known value (e.g. to connect with an external DB tool).
> **HTTPS proxy:** omit `-e AUTH_COOKIE_SECURE=false` (or set it to `true`) when the container sits behind a TLS-terminating reverse proxy.

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
| `SAIC_REGION` | `eu`, `cn`, or `row` |
| `JWT_SECRET` | At least 32 random characters -- generate with `openssl rand -base64 32` |
| `CORS_ORIGIN` | The URL you open in your browser, e.g. `http://192.168.1.100:8080` |

`POSTGRES_PASSWORD` and `MQTT_BROKER_PASSWORD` default to `garagestack` when using the bundled services. Set them explicitly if you want stronger credentials or need to connect to the database/broker from outside the stack.

`VAPID_PUBLIC_KEY` / `VAPID_PRIVATE_KEY` are optional; leave them empty to disable push notifications.

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

The only data that matters for disaster recovery is your PostgreSQL database (all vehicle telemetry/trip history) and the DataProtection keys used to sign login cookies -- losing the keys just logs everyone out, it doesn't lose any vehicle data.

**Docker Compose:** back up the database with `pg_dump` (works whether you're using the bundled Postgres or your own external server):

```bash
docker compose exec postgres pg_dump -U garagestack garagestack > garagestack-backup.sql
```

Restore into a fresh database:

```bash
cat garagestack-backup.sql | docker compose exec -T postgres psql -U garagestack garagestack
```

(Replace `garagestack`/`garagestack` with your `POSTGRES_USER`/`POSTGRES_DB` if you customised them.) The DataProtection keys live in the `api_dataprotection` named volume -- back it up with the API stopped so nothing is mid-write:

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
| Low tyre pressure | Any tyre below 2.2 bar |
| Low EV battery | EV state-of-charge below 20 % |
| Car left unlocked | `doors/locked = false` while engine is off |
| Door left open | Any door, boot, or bonnet open while engine is off |
| Window left open | Any window or sunroof open while engine is off |

Push notifications require VAPID keys to be configured (`VAPID_PUBLIC_KEY` / `VAPID_PRIVATE_KEY`). Without them, alerts still appear in the in-app notification panel. The "engine started" alert is also triggered in real time when the event arrives over MQTT, independently of the 5-minute polling cycle.

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

Log in to GarageStack and open the browser developer tools. The VIN appears in the `/api/vehicles` response, or in the URL when you navigate to your vehicle.

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
| `isCharging` | string | Charging state: `"Charging"` or `"Not charging"` |
| `chargerConnected` | string | Charger connection state: `"Plugged in"` or `"Unplugged"` |
| `mileageSinceLastCharge` | number | Distance driven since last full charge (km) |
| `hvSocKwh` | number | HV battery energy (kWh) |
| `hvTotalCapacityKwh` | number | HV battery total capacity (kWh) |
| `hvVoltage` | number | HV system voltage (V) |
| `hvCurrent` | number | HV system current (A) |
| `hvPower` | number | HV system power (W) |
| `odometerKm` | number | Total odometer reading (km) |
| `mileageOfTheDayKm` | number | Distance driven today (km) |
| `powerUsageOfDayKwh` | number | Energy used today (kWh, converted from raw Wh) |
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

### Caching architecture

All three POI types share the same tile-based PostgreSQL cache:

- The map is divided into a 0.5 deg x 0.5 deg grid (roughly 55 x 40 km at European latitudes).
- Each tile is fetched once and stored for 7 days; subsequent requests for the same area are served from the database with no external API call.
- The background Worker pre-populates tiles around your car on startup and every 6 hours (fuel and service areas only).
- The `MaxOnDemandTiles` cap (1 per API request) prevents Overpass rate-limiting when many uncached tiles are requested at once; the frontend chains requests automatically with back-off when more tiles remain.

---

## Security defaults

- API routes require login.
- Login reuses the configured MG account credentials (`SAIC_USER`/`SAIC_PASSWORD`) and issues short-lived JWT tokens.
- MQTT now requires credentials and ACLs, and broker exposure defaults to localhost-only in Docker Compose.

---

## GitHub Actions / CI

The Docker build workflow requires two repository secrets to avoid Docker Hub anonymous pull rate limits (GitHub runners share IPs and exhaust the limit quickly):

- **`DOCKERHUB_USERNAME`** - Your Docker Hub username
- **`DOCKERHUB_TOKEN`** - A Docker Hub access token (hub.docker.com > Account Settings > Security > New Access Token)

Add them under **Settings > Secrets and variables > Actions** in your fork. A free Docker Hub account is sufficient.

---

> **Development note:** This project was built with AI-assisted development (Claude Code). All code was reviewed, directed, and validated by a human developer throughout - AI acted as a coding assistant, not an autonomous agent.
