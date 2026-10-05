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
| `MQTT_HOST` | Compose: `mosquitto` | Broker the API and Worker connect to. The all-in-one container always uses its own. |
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
| `AUTH_COOKIE_SECURE` | `false` in the Docker deployments | Set `true` when GarageStack is served over HTTPS or behind a TLS-terminating proxy. Outside Docker, cookies are secure unless the API runs in development. |
| `AUTH_SESSION_LIFETIME_HOURS` | `168` | How long a sign-in lasts, from 1 to 8760 hours. |
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

## The car

| Variable | Default | What it does |
| --- | --- | --- |
| `TYRE_PRESSURE_LOW_BAR` / `TYRE_PRESSURE_GOOD_BAR` / `TYRE_PRESSURE_HIGH_BAR` | `2.2` / `2.6` / `3.2` | Tyre pressure bands in bar, for the diagram's colours and the tyre notifications. Set them from your car's placard. |
| `HV_BATTERY_CAPACITY_KWH` | none | Usable size of the traction battery in kWh. The gateway assumes an EV-sized pack, so its kWh figures are far too large on a plain hybrid (an MG HS Hybrid+ carries 1.83 and is reported as 72.5). Empty keeps the gateway's figure on a plug-in car and shows a hybrid's charge as a percentage only. |

## Stored data

| Variable | Default | What it does |
| --- | --- | --- |
| `TELEMETRY_FULL_DETAIL_DAYS` | `365` | Days of telemetry kept exactly as the car reported it. Older telemetry is folded into one row per quarter of an hour, keeping the latest value of every reading, each day's totals and the saved trips. Below `90` is raised to `90`, the furthest back the statistics page and the map read. `0` keeps every row. See [Data kept over time](ARCHITECTURE.md#data-kept-over-time). |

## API, logging and ports

| Variable | Default | What it does |
| --- | --- | --- |
| `RATE_LIMIT_GLOBAL_PER_MINUTE` | `120` | Requests per minute the API accepts from one address. Raise it when several people share one public address. Login and the widget keep their own, tighter limits. |
| `WIDGET_API_KEY` | none | Key for the Homepage dashboard widget endpoint. Empty switches the endpoint off. See [`WIDGET.md`](WIDGET.md). |
| `DEBUG_LOGS` | `false` | `true` turns on debug logging in the API and the Worker. |
| `FRONTEND_PORT` | `8080` | Host port of the web interface (Docker Compose). |
| `API_PORT` | `5000` | Host port of the API, bound to localhost (Docker Compose). The web interface does not need it. |
| `APP_VERSION` | `0.0.0-local` | Version shown in the footer of an image built from source: `APP_VERSION=$(git describe --tags) docker compose up -d --build`. Published images carry their own. |

## Settings without a variable

These are set by their .NET key in environment form.

| Key | Default | What it does |
| --- | --- | --- |
| `Cors__Origins__1`, `Cors__Origins__2`, ... | none | More addresses allowed next to `CORS_ORIGIN`. |
| `ForwardedHeaders__TrustedProxies__0`, ... | the private address ranges | The proxy addresses whose `X-Forwarded-For` and `X-Forwarded-Proto` headers are believed. Set it to your proxy to stop anything else in those ranges from claiming another address. |
| `Vapid__Subject` | `mailto:` plus `SAIC_USER` in the Docker deployments | Contact address sent to push services with every notification. |
