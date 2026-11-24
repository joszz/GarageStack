# Demo Mode

Demo mode runs GarageStack with realistic fake data and requires no MG iSmart credentials, no database, and no MQTT broker.

## What you get

- MG ZS EV with 400 days of history (including occasional home AC and public DC charging sessions with battery and power curves), 21 finished trips from the last five weeks, each leaving from where the one before it ended (out and back from Amsterdam, with day trips to Utrecht and Amersfoort, Den Haag and Delft, and Tilburg and Breda), and one in progress
- Optionally a second car, an MG HS plug-in hybrid parked at home in Utrecht, with its own trips, history, charging, maintenance and live state (`DEMO_SECOND_VEHICLE`, below)
- Dashboard, Statistics, and Map views fully populated
- **Demo Controls** (in the sidebar) to change the car live: doors, windows, lights, lock, driving, charging, battery and fuel, 12 V, temperatures and tyres
- A demo banner across the top reminding you that data is not real

## Docker (quickest start)

```bash
cp .env.demo.example .env.demo
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

The script auto-creates `frontend/.env.development.local` and `.env.demo` from their example files on first run. It starts the API with the settings in `.env.demo`, the same file the Docker stack reads, so the second car, the RDW card or other credentials are set in one place for both. Only the ports and the CORS origin there are the Docker stack's: the script always uses :5000 for the API and :5173 for the frontend. A change to `.env.demo` takes effect when the API starts again.

### Manual start

**Backend:**

```bash
cd src/GarageStack.Api
dotnet run --launch-profile Demo
```

This starts the API at `http://localhost:5000` with `DEMO_MODE=true`, `AUTH_USERNAME=demo`, and `AUTH_PASSWORD=demo` pre-configured. No database or MQTT connection is made. Started this way it does not read `.env.demo`: pass a setting with `-e`, which beats the launch profile, for example `dotnet run --launch-profile Demo -e DEMO_SECOND_VEHICLE=true`.

Open the API reference through the frontend, at [http://localhost:5173/api/reference/](http://localhost:5173/api/reference/) once the frontend below runs: Vite passes `/api` on to the API, so the page shares the app's session and *Test Request* works after signing in. Straight from the API, at `http://localhost:5000/api/reference/`, it cannot sign you in (the login page is the app's), and a change sent from there is refused: that address is not an allowed origin.

**Frontend:**

```bash
cd frontend
cp .env.development.local.example .env.development.local
pnpm dev
```

Open [http://localhost:5173](http://localhost:5173) and log in with **demo / demo**.

The `.env.development.local` file sets `VITE_DEMO_MODE=true` so the demo banner and the Demo Controls are visible. This file is git-ignored by Vite, so it will not be committed.

## Logging in

| Setup | URL | Username | Password |
| --- | --- | --- | --- |
| Docker | [http://localhost:8080](http://localhost:8080) | `demo` | `demo` |
| Local dev | [http://localhost:5173](http://localhost:5173) | `demo` | `demo` |

The credentials come from `AUTH_USERNAME` / `AUTH_PASSWORD` in `.env.demo`, for Docker and for `start-demo.ps1` alike. A bare `dotnet run --launch-profile Demo` uses the `Demo` launch profile's demo / demo instead.

Demo mode deliberately sticks to the built-in password login. Configuring `OIDC_AUTHORITY` would switch the demo over to single sign-on as well -- see [`AUTHENTICATION.md`](AUTHENTICATION.md).

## Demo Controls

Open them with **Demo Controls** in the sidebar (in the menu on a phone). They open in the app's usual dialog and change the car shown, and only that car: with two cars, the title names the one you are changing. A switch takes effect at once, a slider when you let go, and the dashboard follows. Close the dialog (or press Escape) to look at the result.

| Tab | Controls |
| --- | --- |
| Car | Each door, the bonnet and the boot; each window; locked; lights (off, side, dipped, main beam) |
| Driving | Parked or driving, speed 0-200 km/h, engine, online, MG sign-in of the gateway |
| Energy | Cable connected, charging, AC or DC, charging power; battery level, fuel level (on a car with a tank), 12 V battery |
| Climate | Climate on, interior and exterior temperature |
| Tyres | Each tyre's pressure |

The car's state stays in one piece: starting a charge plugs the cable in, pulling the cable ends the charge, a charge shows the power of the car's own wall box until you set another, and driving unplugs the car. Readings show in your units; what is sent is metric.

## How it works

`DEMO_MODE=true` swaps in demo implementations at startup:

| Interface | Demo implementation |
| --- | --- |
| `AppDbContext` | An in-memory database for everything you add yourself: settings, maintenance, climate schedules, home and work places, API tokens, notifications |
| `IVehicleRepository` | Returns the MG ZS EV, and the MG HS PHEV when `DEMO_SECOND_VEHICLE` is on |
| `ITelemetryRepository` | Serves each car's pre-built snapshots and its own live state; mutable per car via `/api/demo/status/{vin}` |
| `ITripRepository` | Serves each car's pre-built trips, and keeps what you change on one car apart from the other |
| `IRdwClient` | Answers for the RDW with any plate: an APK due in three weeks, one open recall and one repaired, so the RDW card shows without a car and without a request leaving the machine. The demo seeds the two notifications the Worker would have sent about it. `RDW_ENABLED=false` leaves the card, the plate field and those notifications out |
| `IMqttPublisher` | Carries commands out on the demo car: after two seconds the car's state changes and the answer arrives, as from the gateway. Starting to charge without the cable plugged in (Demo Controls) is refused, to show a refusal |
| `LogFiles:WorkerDirectory` | When `WORKER_LOG_DIRECTORY` is not set, a folder under the temp directory holding a day of sample Worker log entries (`DemoWorkerLog`), so the log viewer shows both services instead of asking for the Worker's folder |

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
- **The demo car is `DEMOVIN0000012345`.** Change its state as the Demo Controls do, with `POST /api/demo/status/DEMOVIN0000012345` and any of the fields of `DemoStatusOverrideDto`, such as `{"isLocked": false, "evSocPercent": 35}`. A command (`POST /api/vehicles/{vin}/commands/{command}`) is carried out two seconds later, as the gateway would. With `DEMO_SECOND_VEHICLE=true` the second car is `DEMOVIN0000067890`; both calls change only the car they name.
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
| `DEBUG_LOGS` | `false` | `true` logs debug detail from the API |
| `RATE_LIMIT_GLOBAL_PER_MINUTE` | `2000` | Requests per minute from one address. Higher than a real deployment's 120, so the browser tests can drive several pages at once |
| `RDW_ENABLED` | `true` in demo mode | `false` leaves out the RDW card, the licence plate field and the seeded RDW notifications. Unlike a real deployment, where it is off unless set, the demo shows it: its stand-in RDW sends nothing anywhere. |
| `DEMO_SECOND_VEHICLE` | `false` | `true` adds a second car, an MG HS PHEV (`DEMOVIN0000067890`), next to the ZS EV, to try the car picker and notifications that name their car. It tells its own story, so switching cars is plain to see: parked and plugged in at home in Utrecht instead of driving around Amsterdam, its own trips (numbered from 101), a smaller battery it charges every evening on a slower wall box and never at a fast charger, fuel it burns once the battery runs out, its own maintenance and climate schedule, and no licence plate, so its RDW card offers the field to enter one. The demo's phone (the Phone GPS layer) rides with the ZS. |
