# Demo Mode

Demo mode runs GarageStack with realistic fake data and requires no MG iSmart credentials, no database, and no MQTT broker.

## What you get

- MG ZS EV with 30 days of realistic SOC history, 5 pre-built trips around Amsterdam
- Dashboard, Statistics, and Map views fully populated
- A **Demo Controls** panel (bottom-right button) to toggle vehicle states live: doors, windows, lights, lock, engine, charging, SOC, temperature
- A demo banner across the top reminding you that data is not real

## Docker (quickest start)

```bash
cp .env.demo.example .env.demo
# Edit .env.demo and set a real JWT_SECRET (at least 32 characters)
docker compose -f docker-compose.demo.yml up --build
```

Open [http://localhost:8080](http://localhost:8080) and log in with **demo / demo**.

The API is available at `http://localhost:5001`. Scalar API docs are not served in production mode — start the API locally (see below) if you need them.

## Local development (no Docker)

### Quick start (Windows Terminal)

```powershell
.\start-demo.ps1
```

Opens Windows Terminal with two horizontal panes: API on top, frontend on the bottom. Falls back to two separate windows if Windows Terminal is not installed.

The script auto-creates `frontend/.env.development.local` from the example file on first run.

### Manual start

**Backend:**

```bash
cd src/GarageStack.Api
dotnet run --launch-profile Demo
```

This starts the API at `http://localhost:5000` with `DEMO_MODE=true`, `Auth:Username=demo`, and `Auth:Password=demo` pre-configured. No database or MQTT connection is made.

**Frontend:**

```bash
cd frontend
cp .env.development.local.example .env.development.local
pnpm dev
```

Open [http://localhost:5173](http://localhost:5173) and log in with **demo / demo**.

The `.env.development.local` file sets `VITE_DEMO_MODE=true` so the demo banner and Demo Controls panel are visible. This file is git-ignored by Vite, so it will not be committed.

## Logging in

| Setup | URL | Username | Password |
| --- | --- | --- | --- |
| Docker | [http://localhost:8080](http://localhost:8080) | `demo` | `demo` |
| Local dev | [http://localhost:5173](http://localhost:5173) | `demo` | `demo` |

The credentials come from `AUTH_USERNAME` / `AUTH_PASSWORD` in `.env.demo` (Docker) or the `Auth__Username` / `Auth__Password` values in the `Demo` launch profile (local). Change them in either place if you want different credentials.

## Demo Controls panel

Click the flask icon in the bottom-right corner to open the panel. Changes take effect immediately and refresh the dashboard. Available controls:

| Section | Controls |
| --- | --- |
| Doors / Bonnet / Trunk | Toggle each opening open/closed |
| Windows | Toggle each window open/closed |
| Lights | Main beam, dipped beam, sidelights |
| State | Locked, engine running, climate |
| Charging | Charger connected, actively charging |
| Battery SOC | Slider 10-100% |
| Temperature | Interior and exterior (°C) |

## How it works

`DEMO_MODE=true` swaps in three in-memory implementations at startup:

| Interface | Demo implementation |
| --- | --- |
| `IVehicleRepository` | Returns one static MG ZS EV |
| `ITelemetryRepository` | Serves pre-built snapshots and 5 trips; mutable via `/api/demo/status/{vin}` |
| `IMqttPublisher` | No-op (vehicle commands are accepted but not sent) |

No PostgreSQL, Mosquitto, Worker, or SAIC gateway containers are started.

## Environment variables

| Variable | Default | Description |
| --- | --- | --- |
| `DEMO_MODE` | `false` | Set to `true` to activate demo mode |
| `AUTH_USERNAME` | _(required)_ | Login username |
| `AUTH_PASSWORD` | _(required)_ | Login password |
| `JWT_SECRET` | _(required)_ | Min 32 chars, used to sign tokens |
| `FRONTEND_PORT` | `8080` | Host port for the frontend container |
| `API_PORT` | `5001` | Host port for the API container |
| `CORS_ORIGIN` | `http://localhost:8080` | Allowed CORS origin |
