# Configuration reference

Every setting GarageStack reads from its environment, with its default and what it does. The
same names work for both ways of running it: Docker Compose reads them from `.env`, the
all-in-one container and the Unraid template take them as container variables.

- **Leave a variable empty to keep its default.** An empty value counts as unset everywhere, so a
  blank Unraid field or a blank line in `.env` never switches something off by accident.
- **Docker Compose passes each variable only to the containers that use it.** A name that
  `docker-compose.yml` does not list reaches no container, so adding your own name to `.env` has
  no effect until the compose file passes it on too.
- **Settings can also be given by their .NET key.** Each variable fills one setting of the API or
  the Worker, for example `MQTT_HOST` fills `Mqtt:Host`. The full list is in
  [`EnvironmentAliases.cs`](../src/GarageStack.Core/Configuration/EnvironmentAliases.cs). A key
  set directly in .NET's environment form (`Mqtt__Host`) wins over the variable. The all-in-one
  entrypoint relies on that for the values it works out itself, such as its internal broker.

## Required

| Variable | Default | What it does |
| --- | --- | --- |
| `SAIC_USER` | none | Email address of the MG iSmart account the car is registered to. It must be the owner account: a shared account cannot register the alarm switches the gateway needs. |
| `SAIC_PASSWORD` | none | Password of that account. |
| `CORS_ORIGIN` | `http://localhost:8080` | The exact address you open GarageStack on, including the port, for example `http://192.168.1.100:8080`. A browser on any other address is refused. |
| `POSTGRES_PASSWORD` | Compose: none. All-in-one: generated | Database password. Docker Compose refuses to start without it. The all-in-one container generates one on first start and keeps it in `/data/.postgres_password`. |
| `MQTT_BROKER_PASSWORD` | Compose: none. All-in-one: generated | Password of the broker login the services use. Docker Compose refuses to start without it. The all-in-one container generates one on every start unless you set it. |

## MG account and gateway

| Variable | Default | What it does |
| --- | --- | --- |
| `SAIC_REGION` | `eu` | Region the car is registered in: `eu`, `au` or `tr`, mapped to the right MG API endpoint. |
| `SAIC_REST_URI` | from `SAIC_REGION` | The MG API endpoint itself, for a region not in the list above. |

## Database (Docker Compose)

| Variable | Default | What it does |
| --- | --- | --- |
| `POSTGRES_HOST` | `postgres` | Database server. Keep the default for the bundled database (`--profile bundled-postgres`), or point it at your own server. |
| `POSTGRES_PORT` | `5432` | Database port. |
| `POSTGRES_DB` | `garagestack` | Database name. Also used by the all-in-one container. |
| `POSTGRES_USER` | `garagestack` | Database user. Also used by the all-in-one container. |

## MQTT broker

| Variable | Default | What it does |
| --- | --- | --- |
| `MQTT_BROKER_USERNAME` | `garagestack` | Username of the broker login the services use. |
| `MQTT_HOST` | Compose: `mosquitto` | Broker the API and Worker connect to. The gateway container publishes to `mosquitto` (the broker address in its section of `docker-compose.yml`), so change both together. The all-in-one container always uses its own. |
| `MQTT_PORT` | `1883` | Port of that broker. |
| `MQTT_EXTERNAL_PORT` | `1883` | Host port the bundled broker is published on (Docker Compose). |
| `MQTT_BIND_ADDRESS` | `127.0.0.1` | Interface that port is bound to (Docker Compose). Set `0.0.0.0` only to reach the broker from other devices. |
| `HA_MQTT_USERNAME` / `HA_MQTT_PASSWORD` | none | A second broker login for Home Assistant, limited to the car's topics and Home Assistant discovery. Empty skips it. See [`HOME_ASSISTANT.md`](HOME_ASSISTANT.md). |

## Signing in

The full guide, with provider examples, is [`AUTHENTICATION.md`](AUTHENTICATION.md).

| Variable | Default | What it does |
| --- | --- | --- |
| `AUTH_USERNAME` / `AUTH_PASSWORD` | `SAIC_USER` / `SAIC_PASSWORD` | Credentials of the built-in login. |
| `AUTH_PASSWORD_LOGIN_ENABLED` | on while no OIDC provider is set | `true` keeps the built-in login next to single sign-on (break-glass access), `false` switches it off. |
| `AUTH_COOKIE_SECURE` | `auto` in the Docker deployments | `auto` marks the session cookie `Secure` whenever the browser uses HTTPS, also behind a TLS-terminating proxy, and keeps plain HTTP working. `true` always marks it (plain HTTP then cannot log in), `false` never does. Unset outside Docker, cookies are secure unless the API runs in development. |
| `AUTH_SESSION_LIFETIME_HOURS` | `168` | How long a single sign-on (OIDC) session lasts, from 1 to 8760 hours. A password sign-in lasts 12 hours, or 30 days with "Remember me". |
| `OIDC_AUTHORITY` | none | Issuer URL of your identity provider. Setting it switches to single sign-on and turns the built-in login off. |
| `OIDC_CLIENT_ID` / `OIDC_CLIENT_SECRET` | none | Client credentials from the provider. The secret stays empty for a public (PKCE-only) client. |
| `OIDC_ALLOWED_GROUPS` / `OIDC_ALLOWED_EMAILS` | none | Who may sign in. Leave both empty only if the provider itself restricts the application. |
| `OIDC_GROUPS_CLAIM` | `groups` | Claim the allowed groups are matched against. |
| `OIDC_SCOPES` | `openid profile email` | Scopes to request. |
| `OIDC_PROVIDER_NAME` | `SSO` | Name on the sign-in button. |
| `OIDC_AUTO_LOGIN` | `false` | `true` skips the login page and goes straight to the provider. |
| `OIDC_REDIRECT_URI` | worked out per request | Only for setups where GarageStack cannot work out its own address, such as chained proxies. Must end in `/api/auth/oidc/callback`. |
| `OIDC_REQUIRE_HTTPS_METADATA` | `true` | `false` only for a provider on plain HTTP in your own network. |

## Push notifications

| Variable | Default | What it does |
| --- | --- | --- |
| `VAPID_PUBLIC_KEY` / `VAPID_PRIVATE_KEY` | none | Web Push keys, generated once with `npx web-push generate-vapid-keys`. Without both, push stays off and notifications only reach the bell in the app. |
| `VAPID_SUBJECT` | `CORS_ORIGIN` when it is https, otherwise `mailto:admin@garagestack.local` | The contact sent to the push services (Google, Mozilla, Apple) with every notification: `mailto:you@example.com` or an https URL. |
| `NOTIFICATION_LANGUAGE` | `en` | Language of the notification texts: `en` or `nl`. The Worker writes them and has no browser to ask. |

## Map and place names

| Variable | Default | What it does |
| --- | --- | --- |
| `OPENCHARGEMAP_API_KEY` | none | Key for the charging station layer, free at [openchargemap.org](https://openchargemap.org/site/develop). Empty hides the layer. |
| `OVERPASS__BASEURL` | `https://overpass-api.de/api/interpreter` | Overpass endpoint for the fuel station, service area and speed camera layers. Only worth changing for your own instance. |
| `SPEEDCAMERAS__ENABLED` | `true` | `false` removes the speed camera layer from the deployment, for where showing camera positions is restricted. |
| `GEOCODING__ENABLED` | `true` | `false` keeps coordinates away from the geocoder: the trip list falls back to dates. |
| `GEOCODING__BASEURL` | `https://nominatim.openstreetmap.org` | Nominatim endpoint for place names. |
| `MAPMATCHING__ENABLED` | `true` | `false` keeps trip geometry away from the matcher: trips are drawn from their raw fixes. |
| `MAPMATCHING__BASEURL` | `https://valhalla1.openstreetmap.de` | Valhalla endpoint for snapping trips onto roads. |

## Dutch registration (RDW)

For a car with a Dutch licence plate, GarageStack can show when its APK runs out, its open
recalls and whether it is insured, from the [RDW's open data](https://opendata.rdw.nl). Off
until you switch it on. **The plate is the only thing that leaves your network**: it is sent to
`opendata.rdw.nl`, together with the RDW's own reference codes for the recalls it lists. Nothing
about your account, the car's position or its telemetry goes with it. The RDW's data is public,
needs no key and is free to reuse. The plate itself is entered under Settings > Licence plate,
which only appears once this is on.

| Variable | Default | What it does |
| --- | --- | --- |
| `RDW_ENABLED` | `false` | `true` looks the plate up at the RDW: once as soon as it is entered, then by the Worker every `RDW_REFRESH_HOURS`. The dashboard gets an RDW card, and notifications come for a new open recall, the APK a month and a week before it runs out and once it has, and a car with no WAM insurance on record. Off, nothing is sent, and the plate field, the card and the notification type are not shown. |
| `RDW_REFRESH_HOURS` | `24` | Hours a lookup is kept before the Worker asks again. One hour at the least. |
| `RDW_APP_TOKEN` | none | A Socrata app token, sent as `X-App-Token`. Not needed: requests without one work, but share a lower rate limit with everyone else asking anonymously. Free to create with an account on the RDW's open data portal. |

## The car

| Variable | Default | What it does |
| --- | --- | --- |
| `TYRE_PRESSURE_LOW_BAR` / `TYRE_PRESSURE_GOOD_BAR` / `TYRE_PRESSURE_HIGH_BAR` | `2.2` / `2.6` / `3.2` | Tyre pressure bands in bar, for the diagram's colours and the tyre notifications. Set them from your car's placard. |
| `TYRE_PRESSURE_DROP_BAR` | `0.15` | How far, in bar, one tyre may fall behind the other three over two weeks before it is reported as a possible slow puncture. `0` switches the alert off. |
| `AUX_BATTERY_LOW_VOLT` | `12.0` | 12V battery voltage a parked car is warned below, once several polls in a row show it. `0` switches the alert off. |
| `LOW_FUEL_PERCENT` | `15` | Fuel level, in percent, a hybrid or plug-in hybrid is warned below. `0` switches the alert off. |
| `LOW_EV_PERCENT` | `20` | Charge level, in percent, a plug-in car is warned below. `0` switches the alert off. |
| `STALE_DATA_HOURS` | `48` | Hours without a report from the car before GarageStack warns that data has stopped arriving. Keep it above the gateway's poll of a sleeping car, once a day by default. `0` switches the alert off. |
| `HV_BATTERY_CAPACITY_KWH` | none | Usable size of the traction battery in kWh. The gateway assumes an EV-sized pack, so its kWh figures are far too large on a plain hybrid (an MG HS Hybrid+ carries 1.83 and is reported as 72.5). Empty keeps the gateway's figure on a plug-in car and shows a hybrid's charge as a percentage only. |

## Stored data

| Variable | Default | What it does |
| --- | --- | --- |
| `TELEMETRY_FULL_DETAIL_DAYS` | `365` | Days of telemetry kept exactly as the car reported it. Older telemetry is folded into one row per quarter of an hour, keeping the latest value of every reading, each day's totals and the saved trips. Below `90` is raised to `90`, the furthest back the map reads; the statistics read further back, but per day, which compaction keeps. `0` keeps every row. See [Data kept over time](ARCHITECTURE.md#data-kept-over-time). |

## API, logging and ports

| Variable | Default | What it does |
| --- | --- | --- |
| `RATE_LIMIT_GLOBAL_PER_MINUTE` | `120` | Requests per minute the API accepts from one address. Raise it when several people share one public address. Login and the widget keep their own, tighter limits. |
| `UPDATE_CHECK_ENABLED` | `true` | Once a day the API asks GitHub for the latest release, and the footer links to it when it is newer than the running version. `false` keeps the API from contacting GitHub. A build without a version (from source without `APP_VERSION`) never asks. |
| `WIDGET_API_KEY` | none | Key for the Homepage dashboard widget endpoint. Empty switches the endpoint off. See [`WIDGET.md`](WIDGET.md). |
| `DEBUG_LOGS` | `false` | `true` turns on debug logging in the API and the Worker, including a line for every API request. Without it, only refused (4xx), failed (5xx) and slow (over a second) requests are logged. |
| `LOG_RETAINED_FILES` | `30` | Log files each service keeps in its `logs` folder: one per day, and another whenever a day's file passes 100 MB. The oldest go first. |
| `WORKER_LOG_DIRECTORY` | none | Where the API finds the Worker's log files, so the log viewer (the Logs page) shows them as well as its own. The Compose file mounts the Worker's log volume at `/app/worker-logs` and sets this; the all-in-one image sets it itself. Unset, the Logs page shows the API's entries only. |
| `FRONTEND_PORT` | `8080` | Host port of the web interface (Docker Compose). |
| `API_PORT` | `5000` | Host port of the API, bound to localhost (Docker Compose). The web interface does not need it. |
| `APP_VERSION` | `0.0.0-local` | Version shown in the footer of an image built from source: `APP_VERSION=$(git describe --tags) docker compose up -d --build`. Published images carry their own. |

## Building from source

Baked into the frontend bundle when it is built, so they need `docker compose up -d --build` (or `docker build --build-arg ...`) to take effect.

| Variable | Default | What it does |
| --- | --- | --- |
| `MAP_STYLE_DARK` / `MAP_STYLE_LIGHT` / `MAP_STYLE_COLORFUL` | OpenFreeMap's styles | MapLibre style URLs for your own basemap. Add the style's host to `connect-src` and `img-src` in `frontend/nginx-security-headers.conf` as well, or the browser refuses to load it. |
| `VITE_API_URL` | same address as the page | Development only: where `pnpm dev` sends API requests when the API is not behind the Vite proxy. Set it in `frontend/.env.development.local`. |
| `VITE_DEMO_MODE` | `false` | Shows the demo banner and the Demo Controls panel. The demo stack sets it. |

## Running outside Docker

The Docker images set these themselves.

| Variable | Default | What it does |
| --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | none | PostgreSQL connection string for the API and the Worker, for example `Host=localhost;Database=garagestack;Username=garagestack;Password=...`. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` relaxes CORS and CSRF for local work and serves the API reference (Scalar) at `/scalar/v1`; `Demo` serves the reference too. |
| `DEMO_MODE` | `false` | `true` runs the API on built-in fake data, without a database, broker or gateway. See [`DEMO.md`](DEMO.md). |

## Settings without a variable

These are set by their .NET key in environment form. Docker Compose hands a container only the variables `docker-compose.yml` lists, so a line in `.env` does not reach these. Add them in a `docker-compose.override.yml` next to it instead:

```yaml
services:
  api:
    environment:
      ForwardedHeaders__TrustedProxies__0: "192.168.1.10"
```

In the all-in-one container, including Unraid, add them as extra variables on the container.

| Key | Default | What it does |
| --- | --- | --- |
| `Serilog__MinimumLevel__Override__<category>` | see `appsettings.json` | The log level for one source, for example `Serilog__MinimumLevel__Override__GarageStack.Worker.Mqtt=Debug` to see every MQTT message without `DEBUG_LOGS` opening every tap. |
| `Mqtt__ClientId` | `garagestack-worker` | The Worker's MQTT client id. The broker keeps its session and queued messages under it; change it only when two Workers share one broker. |
| `Cors__Origins__1`, `Cors__Origins__2`, ... | none | More addresses allowed next to `CORS_ORIGIN`. |
| `ForwardedHeaders__TrustedProxies__0`, ... | the private address ranges | The proxy addresses whose `X-Forwarded-For` and `X-Forwarded-Proto` headers are believed. Set it to stop anything else in those ranges from claiming another address, and list every proxy in front of the API: the frontend nginx container and, if you run one, your own reverse proxy in front of it. A proxy left out is taken for the visitor, and every visitor then shares its login rate limit. Left unset, sign-ins are also capped at 30 per 5 minutes per device in front of nginx, because any LAN device could otherwise claim a new address on every attempt; setting it turns that cap off. |
