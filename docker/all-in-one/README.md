# GarageStack -- All-in-One Container

Single container that bundles every GarageStack service. Designed for Unraid and similar NAS/homelab environments where running multiple containers is inconvenient.

## What runs inside

| Process | Role |
|---------|------|
| **nginx** | Serves the Vue SPA on port 80; proxies `/api/` to the .NET API |
| **GarageStack.Api** | ASP.NET Core REST API (localhost:9000, not directly exposed) |
| **GarageStack.Worker** | .NET background service -- MQTT consumer, push notifications |
| **PostgreSQL 18** | Embedded database (localhost:5432, not directly exposed) |
| **Mosquitto** | MQTT broker on port 1883 with username/password auth and ACL |
| **SAIC MQTT Gateway** | Python 3.14 service that polls the MG iSmart cloud and publishes telemetry to Mosquitto. Copied directly from the official `saicismartapi/saic-python-mqtt-gateway` image. |

All processes are managed by **supervisord**. Startup order is enforced via priority and startup delays so PostgreSQL is ready before the .NET services try to connect.

## Port layout

| Container port | Purpose | Expose? |
|---------------|---------|---------|
| **80** | Web UI (nginx) | Yes -- map to your chosen host port (default 8080) |
| **1883** | MQTT broker | Optional -- only needed for external MQTT clients such as [Home Assistant](../../documentation/HOME_ASSISTANT.md); keep closed unless needed |

## Persistent data

Mount a single volume at `/data`. The container creates the following layout inside it automatically:

```
/data/
  db/
    postgres/     PostgreSQL data directory
    mosquitto/    Mosquitto retained messages
  logs/           All application and service logs
  dataprotection/ ASP.NET Core Data Protection keys
  .postgres_password   Auto-generated DB password (if not set via env var)
  .db_initialized      Marker written after full database setup completes
```

## Environment variables

### Required

| Variable | Description |
|----------|-------------|
| `SAIC_USER` | MG iSmart account email (must be the vehicle owner account) |
| `SAIC_PASSWORD` | MG iSmart account password |
| `SAIC_REGION` | Region the car is registered in: `eu` (default), `au`, or `tr` -- automatically mapped to the right API endpoint |
| `CORS_ORIGIN` | Exact URL you use to open the app, e.g. `http://192.168.1.100:8080` |

By default the web login uses the same `SAIC_USER` and `SAIC_PASSWORD` credentials, and there is no separate signup flow. Set `AUTH_USERNAME` / `AUTH_PASSWORD` for dedicated credentials, or hand sign-in over to your own identity provider with the `OIDC_*` variables below -- see [`AUTHENTICATION.md`](../../documentation/AUTHENTICATION.md).

### Optional

Every other variable works here as well, under the same name as in Docker Compose. They are all listed, with their defaults, in [`CONFIGURATION.md`](../../documentation/CONFIGURATION.md): push notifications, single sign-on, the map's data sources, tyre pressure bands, the traction battery's real size, the rate limit and the Home Assistant broker login. A few behave differently in this image:

| Variable | In this image |
|----------|---------------|
| `POSTGRES_PASSWORD` | Generated on first start and saved to `/data/.postgres_password` when not set. Set it only if you need a known value, for example to connect an external tool to the database. |
| `MQTT_BROKER_PASSWORD` | Generated on every start when not set. Set it if you publish port 1883 and connect other clients. |
| `MQTT_HOST` / `MQTT_PORT` | Ignored: the services always use the broker inside the container. |
| `AUTH_COOKIE_SECURE` | Defaults to `false`, so a plain-HTTP LAN install works. Set it to `true` behind a TLS-terminating proxy. |

## Building the image

The Dockerfile must be built from the **repository root**, not from this directory, because it copies source from `frontend/` and `src/`.

macOS/Linux:

```bash
docker build \
  -f docker/all-in-one/Dockerfile \
  -t garagestack-aio:local \
  .
```

Windows (PowerShell):

```powershell
docker build `
  -f docker/all-in-one/Dockerfile `
  -t garagestack-aio:local `
  .
```

## Running

### macOS/Linux

```bash
docker run -d \
  --name garagestack \
  --restart unless-stopped \
  -p 8080:80 \
  -v ./garagestack-data:/data \
  -e SAIC_USER=your@email.com \
  -e SAIC_PASSWORD=yourpassword \
  -e SAIC_REGION=eu \
  -e CORS_ORIGIN=http://localhost:8080 \
  ghcr.io/joszz/garagestack:latest
```

### Windows (PowerShell)

> **Important:** always use PowerShell, **not Git Bash**. Git Bash (MSYS) mangles the container-side path `/data` into a Windows path, which silently breaks the volume mount.

```powershell
docker run -d `
  --name garagestack `
  --restart unless-stopped `
  -p 8080:80 `
  -v "${PWD}\garagestack-data:/data" `
  -e SAIC_USER=your@email.com `
  -e SAIC_PASSWORD=yourpassword `
  -e SAIC_REGION=eu `
  -e CORS_ORIGIN=http://localhost:8080 `
  ghcr.io/joszz/garagestack:latest
```

> `POSTGRES_PASSWORD` is omitted -- a random password is auto-generated on first start and saved to `/data/.postgres_password`. Pass `-e POSTGRES_PASSWORD=yourpassword` if you need a known value (e.g. to connect with an external DB tool).

Once running, open `http://localhost:8080` in your browser. First start takes 20-30 seconds for PostgreSQL to initialise and for the .NET services to come up.

## Managing the container

### View live logs (PowerShell or bash)

```powershell
docker logs -f garagestack
```

To view a specific service log from inside the container (all logs are written to the `/data/logs/` bind-mounted directory):

```powershell
# Tail the API log directly from the host
Get-Content -Wait garagestack-data\logs\api-supervisor.log
```

### Stop and start

```powershell
docker stop garagestack
docker start garagestack
```

The container picks up where it left off on restart -- the database and data protection keys are persisted in the mounted `garagestack-data` directory.

### Remove

```powershell
docker rm -f garagestack
```

This removes the container but **keeps** `garagestack-data`. To also wipe the data:

```powershell
docker rm -f garagestack
Remove-Item -Recurse -Force garagestack-data
```

### Backup and restore

Everything -- the PostgreSQL database, Mosquitto retained messages, logs, and DataProtection keys -- lives under `garagestack-data`. Stop the container first so nothing is mid-write, then copy the whole directory:

```powershell
docker stop garagestack
Copy-Item -Recurse garagestack-data garagestack-data-backup
docker start garagestack
```

```bash
docker stop garagestack
cp -r garagestack-data garagestack-data-backup
docker start garagestack
```

To restore, stop the container, replace `garagestack-data` with the backup, and start it again. Only the database (`garagestack-data/db/postgres`) and DataProtection keys (`garagestack-data/dataprotection`) actually matter for disaster recovery -- losing the keys just logs everyone out, it doesn't lose any vehicle data.

## Troubleshooting

### 28P01: password authentication failed for user "garagestack"

The API crash-loops with this error when it cannot connect to the embedded PostgreSQL instance. This most commonly happens when the data directory (`garagestack-data`) contains a partially-initialised database cluster from a previous container version that crashed during first-run setup.

**Fix:** wipe the data directory and let the container reinitialise from scratch.

PowerShell:

```powershell
docker rm -f garagestack
Remove-Item -Recurse -Force garagestack-data
```

Linux/macOS:

```bash
docker rm -f garagestack
rm -rf garagestack-data
```

Then run the container again. The database, credentials, and data protection keys will all be regenerated automatically.

> Note: if the `garagestack-data` directory was created by Docker on Linux (including WSL2), the files inside are owned by Linux UIDs and may not be deletable from Windows Explorer or PowerShell. Use the commands above which force-remove them.

### Container starts but the web UI returns 502

PostgreSQL or the .NET API is still starting up. The API has a 20-second startup delay built in; allow up to 30 seconds after `docker start`. Watch the logs:

```powershell
docker logs -f garagestack
```

Look for `[garagestack] Starting all services via supervisord...` and then all six services reaching `RUNNING` state in the supervisord output.

### Cannot log in with my MG iSmart credentials

Unless you configured something else, the web login uses your `SAIC_USER` / `SAIC_PASSWORD` environment variables directly -- there is no separate account. Check that those values match exactly what you use in the MG iSmart app. The SAIC gateway in the container will also need a few minutes to establish its first session with the MG cloud.

Note that setting `OIDC_AUTHORITY` replaces this login: the page then offers only "Sign in with ...", unless `AUTH_PASSWORD_LOGIN_ENABLED=true` keeps both. See [`AUTHENTICATION.md`](../../documentation/AUTHENTICATION.md).

## Unraid Community Apps

Import the template from `unraid/garagestack.xml`. Fill in at minimum:

1. **MG iSmart Email / Password / Region**
2. **App URL** -- the address you use in your browser (e.g. `http://192.168.1.50:8080`)

**Database Password** is optional -- if left blank a strong random password is auto-generated on first start and saved to `/data/.postgres_password`. You only need to set it explicitly if you want to connect to the embedded database with an external tool.

VAPID keys are optional; leave them blank to skip push notifications.

The **OIDC** fields under "Show advanced settings" are optional too. Fill them in to sign in through Authentik, Authelia, Keycloak or any other OpenID Connect provider instead of the built-in login -- see [`AUTHENTICATION.md`](../../documentation/AUTHENTICATION.md).

> **Note on the MG account:** see the [MG iSmart account and session limits](../../README.md#mg-ismart-account-and-session-limits) section in the main README.
