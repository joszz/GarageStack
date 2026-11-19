# Demo Mode

Demo mode uses the local telemetry CSV for vehicle history, charging sessions, and trips when it is present. If it is absent, the original generated demo data is used. It requires no MG iSmart credentials, database, or MQTT broker.

## What you get

- Vehicle telemetry, charging sessions, and trip routes from `telemetry/telemetry.csv` when present; otherwise the original generated mock data
- Dashboard, Statistics, and Map views fully populated
- A **Demo Controls** panel (bottom-right button) to toggle vehicle states live: doors, windows, lights, lock, engine, charging, SOC, temperature
- A demo banner across the top reminding you that data is not real

## Docker (quickest start)

```bash
cp .env.demo.example .env.demo
mkdir -p telemetry
# Copy your telemetry.csv into ./telemetry/telemetry.csv
docker compose --env-file .env.demo -f docker-compose.demo.yml up --build
```

Open [http://localhost:8080](http://localhost:8080) and log in with **demo / demo**.

The API reference is at [http://localhost:8080/api/reference/](http://localhost:8080/api/reference/): it sends you to sign in first, and then *Test Request* tries any endpoint on the demo car. The API itself also listens on `http://localhost:5001`. See [API.md](API.md).

This stack is also what the Playwright smoke tests run against, in CI and locally, because it is the production frontend image with the real nginx config. See [CONTRIBUTING.md](../.github/CONTRIBUTING.md).

## Local development (no Docker)

### Quick start (Windows Terminal)

```powershell
.\start-demo.ps1
```

Opens Windows Terminal with three panes: a menu on the left, and the API above the frontend on the right. Without Windows Terminal it opens the API and the frontend in two separate windows and runs the menu in the current one.

The script auto-creates `frontend/.env.development.local` from the example file on first run.

### Manual start

**Backend:**

```bash
cd src/GarageStack.Api
dotnet run --launch-profile Demo
```

This starts the API at `http://localhost:5000` with `DEMO_MODE=true`, `Auth:Username=demo`, and `Auth:Password=demo` pre-configured. No database or MQTT connection is made.

Open the API reference through the frontend, at [http://localhost:5173/api/reference/](http://localhost:5173/api/reference/) once the frontend below runs: Vite passes `/api` on to the API, so the page shares the app's session and *Test Request* works after signing in. Straight from the API, at `http://localhost:5000/api/reference/`, it cannot sign you in (the login page is the app's), and a change sent from there is refused: that address is not an allowed origin.

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

Demo mode deliberately sticks to the built-in password login. Configuring `OIDC_AUTHORITY` would switch the demo over to single sign-on as well -- see [`AUTHENTICATION.md`](AUTHENTICATION.md).

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
| Speed | Slider 0-200 km/h |
| Temperature | Interior and exterior (°C) |

## How it works

`DEMO_MODE=true` swaps in demo implementations at startup:

| Interface | Demo implementation |
| --- | --- |
| `AppDbContext` | An in-memory database for everything you add yourself: settings, maintenance, climate schedules, home and work places, API tokens, notifications |
| `IVehicleRepository` | Returns one static MG ZS EV |
| `ITelemetryRepository` | Serves the CSV snapshots; mutable via `/api/demo/status/{vin}` |
| `ITripRepository` | Derives trips and odometer readings from the CSV |
| `IRdwClient` | Answers for the RDW with any plate: an APK due in three weeks, one open recall and one repaired, so the RDW card shows without a car and without a request leaving the machine. The demo seeds the two notifications the Worker would have sent about it. `RDW_ENABLED=false` leaves the card, the plate field and those notifications out |
| `IMqttPublisher` | Carries commands out on the demo car: after two seconds the car's state changes and the answer arrives, as from the gateway. Starting to charge without the cable plugged in (Demo Controls) is refused, to show a refusal |
| `LogFiles:WorkerDirectory` | When `WORKER_LOG_DIRECTORY` is not set, a folder under the temp directory holding a day of sample Worker log entries (`DemoWorkerLog`), so the log viewer shows both services instead of asking for the Worker's folder |

The demo Compose file mounts the `telemetry/` directory read-only and sets `DEMO_TELEMETRY_CSV` for the API. Put your capture at `telemetry/telemetry.csv`; the file is local-only and is not included in the repository or image. If the file is absent, the demo uses its generated telemetry, charge history, trip routes, and phone fixes instead. A present but invalid CSV fails explicitly rather than silently switching to mock data.

No PostgreSQL, Mosquitto, Worker, or SAIC gateway containers are started.

## From a script or an AI agent

Demo mode is the quickest way to see a change working, for a coding agent as much as for a person. What either needs to know:

- **Start it without the menu.** `start-demo.ps1` opens Windows Terminal panes, which an agent cannot drive. Start the two halves as background processes instead, from the repository root (copy `frontend/.env.development.local.example` to `frontend/.env.development.local` first, for the demo banner and Demo Controls):

  ```bash
  dotnet run --project src/GarageStack.Api --launch-profile Demo   # the API, on :5000
  pnpm --dir frontend dev                                          # the app, on :5173; passes /api and /hubs to :5000
  ```

  Or the Docker stack, with the production nginx config: `docker compose --env-file .env.demo -f docker-compose.demo.yml up -d --build` (the app on :8080).
- **Look before starting another.** Something answering on :5000 is a demo API already running, perhaps the developer's own. A running API also locks `src/GarageStack.Api/bin/`, so `dotnet build` there fails with MSB3027, "the file is locked by GarageStack.Api". Use the one that runs, or build and run your copy from a git worktree on a port of its own: `dotnet run --launch-profile Demo --urls http://localhost:5099`, with `Cors__Origins__1=http://localhost:5099` set when its own pages (the API reference) should be able to change data.
- **Sign in from a script.** Nearly every endpoint needs a session. The login is the password one, demo / demo; a request without an `Origin` header, as curl sends it, passes the origin check:

  ```bash
  curl -c jar -H 'Content-Type: application/json' -d '{"username":"demo","password":"demo"}' http://localhost:5000/api/auth/login
  curl -b jar http://localhost:5000/api/vehicles
  ```

- **Learn the API from [`openapi.json`](openapi.json)**, the committed OpenAPI document: every path, what it takes and answers, and the refusals with their codes. A running API serves the same at `/api/openapi/v1.json`, and as a page at `/api/reference/`.
- **The demo car is `DEMOVIN0000012345`.** Change its state as the Demo Controls panel does, with `POST /api/demo/status/DEMOVIN0000012345` and any of the fields of `DemoStatusOverrideDto`, such as `{"isLocked": false, "evSocPercent": 35}`. A command (`POST /api/vehicles/{vin}/commands/{command}`) is carried out two seconds later, as the gateway would.
- **Nothing is kept.** What you add lives in memory and is gone when the API stops; the seeded data is the same on every start.
- **A new field or feature needs demo data as well**, in `src/GarageStack.Data/Demo/`, or it never shows here. A change to trips also needs its step in `TripRepositoryContractTests`, which holds `DemoTripRepository` to the same results as the database.
- **To see a page**, run the API and the app and point a throwaway Playwright script at `http://localhost:5173` (the frontend's own `@playwright/test`, with the Chromium it installed): sign in at `/login` with demo / demo, then capture what you need. The tests in `frontend/e2e` run against the Docker stack instead.

## Environment variables

| Variable | Default | Description |
| --- | --- | --- |
| `DEMO_MODE` | `false` | Turns demo mode on. `docker-compose.demo.yml` and the `Demo` launch profile always set it, so it only matters for an API started some other way. |
| `AUTH_USERNAME` | `demo` | Login username |
| `AUTH_PASSWORD` | `demo` | Login password |
| `FRONTEND_PORT` | `8080` | Host port for the frontend container |
| `API_PORT` | `5001` | Host port for the API container |
| `CORS_ORIGIN` | `http://localhost:8080` | Allowed CORS origin |
| `RDW_ENABLED` | `true` in demo mode | `false` leaves out the RDW card, the licence plate field and the seeded RDW notifications. Unlike a real deployment, where it is off unless set, the demo shows it: its stand-in RDW sends nothing anywhere. |
