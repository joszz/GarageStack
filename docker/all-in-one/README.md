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
| **1883** | MQTT broker | Optional -- only needed for external MQTT clients; keep closed unless needed |

## Persistent data

Mount a single volume at `/data`. The container creates the following layout inside it automatically:

```
/data/
  db/
    postgres/     PostgreSQL data directory
    mosquitto/    Mosquitto retained messages
  logs/           All application and service logs
  dataprotection/ ASP.NET Core Data Protection keys
```

## Environment variables

### Required

| Variable | Description |
|----------|-------------|
| `SAIC_USER` | MG iSmart account email (must be the vehicle owner account) |
| `SAIC_PASSWORD` | MG iSmart account password |
| `SAIC_REGION` | Region: `eu`, `cn`, or `row` (default: `eu`) |
| `JWT_SECRET` | Token signing key, minimum 32 characters. Generate: `openssl rand -base64 32` |
| `CORS_ORIGIN` | Exact URL you use to open the app, e.g. `http://192.168.1.100:8080` |

The web login uses the same `SAIC_USER` and `SAIC_PASSWORD` credentials. There is no separate signup flow.

### Optional

| Variable | Description |
|----------|-------------|
| `POSTGRES_PASSWORD` | Password for the embedded database. If not set, a random password is auto-generated on first start and saved to `/data/.postgres_password`. Set explicitly if you need a known value (e.g. for external tools that connect directly to the database). |
| `VAPID_PUBLIC_KEY` | Web Push VAPID public key. Leave empty to disable push notifications. Generate: `npx web-push generate-vapid-keys` |
| `VAPID_PRIVATE_KEY` | Web Push VAPID private key |
| `WIDGET_API_KEY` | Static API key for the Homepage dashboard widget endpoint (`/api/widget/{vin}/status`). Leave empty to disable. Generate: `openssl rand -base64 32` |
| `OPENCHARGEMAP_API_KEY` | API key for the EV charging station map overlay, sourced from [Open Charge Map](https://openchargemap.org/site/develop). Free to obtain. Leave empty to disable the feature. |
| `OVERPASS__BASEURL` | Overpass API endpoint used for the fuel station and motorway service area map overlays. Defaults to the public endpoint (`https://overpass-api.de/api/interpreter`). Set this only if you self-host an Overpass instance. No API key is required for the default public endpoint. |
| `POSTGRES_DB` | Database name (default: `garagestack`) |
| `POSTGRES_USER` | Database user (default: `garagestack`) |

## Building the image

The Dockerfile must be built from the **repository root**, not from this directory, because it copies source from `frontend/` and `src/`:

```bash
docker build \
  -f docker/all-in-one/Dockerfile \
  -t ghcr.io/joszz/garagestack:latest \
  .
```

## Running locally for testing

macOS/Linux (bash):

```bash
docker run -d \
  --name garagestack \
  -p 8080:80 \
  -v ./garagestack-data:/data \
  -e SAIC_USER=your@email.com \
  -e SAIC_PASSWORD=yourpassword \
  -e SAIC_REGION=eu \
  -e JWT_SECRET="$(openssl rand -base64 32)" \
  -e CORS_ORIGIN=http://localhost:8080 \
  ghcr.io/joszz/garagestack:latest
```

Windows (PowerShell -- `openssl` is not available by default and `$()` substitution is bash-only):

```powershell
$jwtSecret = [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Minimum 0 -Maximum 256 }))
docker run -d `
  --name garagestack `
  -p 8080:80 `
  -v ${PWD}/garagestack-data:/data `
  -e SAIC_USER=your@email.com `
  -e SAIC_PASSWORD=yourpassword `
  -e SAIC_REGION=eu `
  -e JWT_SECRET=$jwtSecret `
  -e CORS_ORIGIN=http://localhost:8080 `
  ghcr.io/joszz/garagestack:latest
```

> `POSTGRES_PASSWORD` is omitted here -- a random password is auto-generated on first start and saved to `/data/.postgres_password`. Pass `-e POSTGRES_PASSWORD=yourpassword` explicitly if you need a known value.

## Unraid Community Apps

Import the template from `unraid/garagestack.xml`. Fill in at minimum:

1. **MG iSmart Email / Password / Region**
2. **JWT Secret** -- generate with `openssl rand -base64 32`
3. **App URL** -- the address you use in your browser (e.g. `http://192.168.1.50:8080`)

**Database Password** is optional -- if left blank a strong random password is auto-generated on first start and saved to `/data/.postgres_password`. You only need to set it explicitly if you want to connect to the embedded database with an external tool.

VAPID keys are optional; leave them blank to skip push notifications.

> **Note on the MG account:** see the [MG iSmart account and session limits](../../README.md#mg-ismart-account-and-session-limits) section in the main README.
