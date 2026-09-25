# Architecture

This is an overview of how GarageStack's services fit together, for contributors who need
the map before their first PR. For end-user setup, see [README.md](../README.md); for local dev
without a real car, see [DEMO.md](DEMO.md).

## Services

```text
                     ┌──────────────────────┐
                     │   SAIC / MG cloud     │
                     └──────────┬────────────┘
                                │ polls
                     ┌──────────▼────────────┐
                     │  saic-mqtt-gateway     │  (third-party image, pinned version)
                     └──────────┬────────────┘
                                │ publishes telemetry / subscribes to commands
                     ┌──────────▼────────────┐
                     │      Mosquitto         │  MQTT broker
                     └────┬─────────────┬─────┘
                          │             │
              subscribes  │             │  publishes commands
                     ┌────▼───┐     ┌───▼────┐
                     │ Worker │     │  Api   │◄──── frontend (Vue SPA, via nginx)
                     └────┬───┘     └───┬────┘
                          │  writes     │  reads/writes,
                          │             │  LISTENs for pg_notify
                     ┌────▼─────────────▼────┐
                     │      PostgreSQL         │
                     └─────────────────────────┘
```

- **saic-mqtt-gateway** (`saicismartapi/saic-python-mqtt-gateway`, pinned in `docker-compose.yml`) polls the SAIC/MG cloud API on GarageStack's behalf and publishes telemetry to MQTT, and relays commands published back to MQTT to the cloud API. This is the only piece of the stack that isn't part of this repo.
- **Mosquitto** is the MQTT broker all telemetry and commands flow through. Requires auth; not exposed to the LAN by default. Its password and ACL files are generated on every start by [docker/mosquitto-auth.sh](../docker/mosquitto-auth.sh), shared by Compose and the all-in-one image. An optional, restricted second login lets Home Assistant consume the gateway's MQTT discovery directly (see [HOME_ASSISTANT.md](HOME_ASSISTANT.md)).
- **Worker** (`GarageStack.Worker`) subscribes to MQTT and writes telemetry to Postgres. Runs on its own, no inbound HTTP.
- **Api** (`GarageStack.Api`) serves the REST API and SignalR hub the frontend talks to, and separately publishes outbound MQTT messages for remote commands (lock, climate, etc.).
- **Postgres** is the only datastore. The Api also uses it as a pub/sub channel (`pg_notify`/`LISTEN`) to learn about writes the Worker makes, so it can push live updates without polling the DB.
- **frontend** is a Vue 3 SPA built and served by nginx; the only service with a public port by default.

## Data flow: a telemetry update reaching the browser

1. `saic-mqtt-gateway` polls the SAIC cloud and publishes one MQTT message per changed field to a topic like `saic/{user}/vehicles/{vin}/drivetrain/soc`.
2. `MqttConsumerService` ([src/GarageStack.Worker/Mqtt/MqttConsumerService.cs](../src/GarageStack.Worker/Mqtt/MqttConsumerService.cs)) is subscribed to `saic/#`. It extracts the VIN and subtopic, maps the payload onto a `TelemetrySnapshot` field via `TelemetryMapper`, and writes it to Postgres. A single poll cycle produces several MQTT messages in quick succession, so messages arriving within a 15s window are merged into one row instead of one row per field (see the comments on `MergeOrAddTelemetryAsync` in that file for why this needs a per-vehicle lock).
3. Each write calls `pg_notify('telemetry_updated', vehicleId)`.
4. `TelemetryNotificationService` ([src/GarageStack.Api/Services/TelemetryNotificationService.cs](../src/GarageStack.Api/Services/TelemetryNotificationService.cs)), a background service in the Api process, holds a `LISTEN telemetry_updated` connection. On notification it debounces briefly (to coalesce a poll cycle's several writes into one update), re-reads the merged latest snapshot, and broadcasts it over SignalR to browsers subscribed to that vehicle's group.
5. The frontend's `useSignalR` composable ([frontend/src/composables/useSignalR.ts](../frontend/src/composables/useSignalR.ts)) receives the `telemetryUpdated` event and updates the UI. There is no polling fallback - if the SignalR connection drops, the dashboard goes stale until it reconnects.

The same `pg_notify`/`LISTEN`/SignalR pattern also carries `notification_created` (push/in-app alerts) and `trip_completed` events. The channel names live in `PgChannels` and every publisher goes through the `NotifyAsync` extension in [src/GarageStack.Data/Extensions/PgNotifyExtensions.cs](../src/GarageStack.Data/Extensions/PgNotifyExtensions.cs), which is a no-op on the in-memory provider used by demo mode and the tests. The `notification_created` payload is the `NotificationCreatedPayload` record in Core, serialized by the Worker and deserialized by the Api, so the two processes cannot disagree about its shape.

Push notification texts are written by the Worker, which has no browser request to take a language from; they come from `Resources/NotificationStrings*.resx` in the Worker project and the language is the `NOTIFICATION_LANGUAGE` deployment setting. The marker types for these resources (`NotificationStrings`, and `WidgetStrings` in the Api) must stay in their assembly's root namespace: the resource localizer maps a marker in a sub-namespace to a resource path that does not exist and silently falls back to the keys.

## Sending a command to the car (the reverse path)

The frontend calls `POST /api/vehicles/{vin}/commands/{command}` on the Api, which publishes an MQTT message via `MqttPublisher` ([src/GarageStack.Api/Services/MqttPublisher.cs](../src/GarageStack.Api/Services/MqttPublisher.cs)) - a separate MQTT client from the Worker's, since the Api only ever publishes and the Worker only ever consumes. `saic-mqtt-gateway` picks the message up and relays it to the SAIC cloud API. `VehicleCommandGate` ([src/GarageStack.Api/VehicleCommandGate.cs](../src/GarageStack.Api/VehicleCommandGate.cs)) serializes commands per VIN server-side, since the real gateway only processes one command at a time.

## Projects under `src/`

| Project | Contains | Depends on |
| --- | --- | --- |
| `GarageStack.Core` | Domain models (`Models/`), repository/service interfaces (`Interfaces/`), and pure helpers with no I/O (`Helpers/`) - the shared vocabulary every other project builds on. | nothing (leaf project) |
| `GarageStack.Data` | EF Core: `AppDbContext`, migrations, concrete repository implementations, and `Demo/` (in-memory fakes used when `DEMO_MODE=true`). | `Core` |
| `GarageStack.Worker` | The MQTT-ingestion process: `Mqtt/MqttConsumerService`, plus background services for maintenance reminders, POI pre-caching, and push-notification checks. | `Core`, `Data` |
| `GarageStack.Api` | ASP.NET Core minimal APIs (`Endpoints/`), the SignalR hub (`Hubs/`), and Api-only services (outbound MQTT publishing, POI/charging-station lookups, the Postgres-LISTEN-to-SignalR bridge). | `Core`, `Data` |
| `GarageStack.Tests` | xUnit tests across all of the above. | all four |

## Caching

There's no Redis (or other external cache) in the stack, by design rather than oversight: this
is a single-instance, single-user deployment, so a distributed cache buys nothing a
process-local one doesn't already provide. Two caches exist today:

- `ITelemetryRepository.GetMergedLatestAsync` (the hot path behind `/status`, the homepage
  widget, and every SignalR broadcast) uses a short-TTL `IMemoryCache` entry, invalidated
  immediately on every telemetry write so it can never serve data older than the write that
  triggered a SignalR broadcast.
- Map POI tiles (charging stations, fuel stations, service areas, speed cameras) are cached in Postgres itself
  (`PoiCacheTile`/`PoiItem`), not in memory, since that data needs to survive process restarts
  and be queried by bounding box - a job a plain in-memory cache isn't suited for anyway. The
  brand filter list is a DISTINCT over the `Brand` column, extracted from the upstream metadata
  at ingest, with a short in-memory cache on top that every tile upsert invalidates.
- Reverse-geocoded place names (`GeocodeCacheEntry`) are cached in Postgres for the same reason,
  keyed by a per-precision coordinate grid plus language. Nominatim's usage policy requires
  caching rather than re-requesting, and a 90-day TTL is honest for data that changes when a
  street is renamed. Only the cache key is quantised to the grid; the coordinate sent upstream is
  the caller's own, so an answer describes the real spot rather than a grid corner.
- Snapped trip lines (`MapMatchCacheEntry`) are cached in Postgres too, keyed by a hash of the
  fixes that were sent rather than by vehicle or timestamp: a finished trip is the same trace
  however often it is selected, and two browsers looking at it share one row. The line is stored
  as an encoded polyline, which keeps a few thousand vertices to a few kilobytes both in the table
  and on the wire. The speed limits along it travel beside it as run-length pairs (`SpeedLimitRuns`)
  for the same reason: a limit holds for a whole stretch of road, so a trip is a few dozen runs
  rather than a value per vertex. `MapMatchDefaults.CacheVersion` is part of the trace hash, so
  asking the matcher for something new (as the limits did) leaves the older rows unreachable instead
  of serving answers that no longer hold everything the client expects.
- The status query's fallbacks are index-backed. Charging and heating schedules are only
  published when the user changes one, so most vehicles have no such row at all: a filtered index
  (`IX_TelemetrySnapshots_VehicleId_RecordedAt_Schedule`) keeps that lookup from reading the
  entire history on every cache miss, which is after every telemetry write.
- Session revocation (`TokenRevocation`) is checked by the cookie handler on every authenticated
  request. The answer is cached in memory: a revocation made by this process is written to the
  cache immediately, a "not revoked" answer is trusted for five minutes, so the check costs no
  database round trip per request.

If GarageStack ever needs to run more than one Api/Worker instance, revisit this: per-vehicle
in-memory state (this cache, the revocation cache, `VehicleCommandGate`,
`NotificationCooldownGate`, the shared `UpstreamRateGate` behind the Overpass/OCM/Nominatim clients)
would all need to move to something shared.

## Authentication

`src/GarageStack.Api/Authentication/` wires both sign-in methods into a single session scheme, so nothing downstream has to care which one was used.

- **Sessions** are ASP.NET Core cookie-auth tickets (`garagestack-auth`), encrypted with the Data Protection keys on disk. `AuthenticationSetup` answers unauthenticated API calls with 401 instead of the framework's default redirect to a login page, checks every request's session id against the `RevokedTokens` table, and keeps the principal slim: display name, subject, session id.
- **OIDC** uses the stock `AddOpenIdConnect` handler (authorization code + PKCE, `response_mode=query` so the callback carries `SameSite=Lax` cookies on plain HTTP). `OidcOptions` binds the `OIDC_*` environment variables and fails fast on a half-finished configuration; `OidcAccessPolicy` decides whether an authenticated user is also authorised, and is the piece worth reading first.
- **Password login** is the fallback. `PasswordLoginOptions.Resolve` switches it off as soon as OIDC is configured, unless `AUTH_PASSWORD_LOGIN_ENABLED` explicitly asks for both, and the API refuses to start when that leaves no way in at all.

Both `/api/auth/oidc/login` and the callback answer failures with a redirect to `/login?error=...` rather than an exception: they are browser navigations, and a JSON 500 is useless to the person looking at it (and, with auto-login on, loops).

The callback lives at `/api/auth/oidc/callback` rather than the handler's default `/signin-oidc` because nginx only proxies `/api/` and `/hubs/` to the API. Operator-facing documentation is in [`AUTHENTICATION.md`](AUTHENTICATION.md); `AuthenticationFlowTests` drives the whole round-trip against a fake provider.

## Database

Code-first EF Core migrations live in `src/GarageStack.Data/Migrations/`. `Program.cs` runs `db.Database.MigrateAsync()` on Api startup in normal operation; in `DEMO_MODE` it calls `DemoSeeder.SeedAsync()` instead, which creates the in-memory schema and seeds fake data, bypassing a real Postgres server entirely.

The Data project has its own `DesignTimeDbContextFactory`, so the EF tooling never needs the Api's configuration:

```sh
dotnet ef migrations add <Name> --project src/GarageStack.Data --startup-project src/GarageStack.Data
dotnet ef migrations has-pending-model-changes --project src/GarageStack.Data --startup-project src/GarageStack.Data
```

CI runs the second command, and the pending-model-changes warning is not suppressed anywhere, so a model change without a migration fails before it reaches a real database.

### Adding a telemetry field

A new value arriving from the car passes through several layers, in this order:

1. `TelemetrySnapshot` (the model) plus a migration, as above.
2. `TelemetryMapper` in the Worker, which maps the MQTT topic onto the new property.
3. The frontend's `TelemetrySnapshot` interface in `services/vehicleApi.ts`, mirrored by hand.
4. Wherever it should show up: a card in `frontend/src/cards/registry.ts`, and the demo data in
   `src/GarageStack.Data/Demo/` so the field is visible without a car.

What it does *not* need is a mention in the repository's merge loops or its "does this row carry
anything" filters, or in `WidgetStatusDto`: the first two are derived from the model itself, and
the widget deliberately serves a curated subset. Charts are the same story in reverse: add the
field to `TelemetryHistoryPoint` and the history query starts returning rows that carry it.

## Frontend

REST calls go through `frontend/src/services/` - `apiCore.ts` centralizes the `fetch` wrapper (cookie-based auth, shared 401 handling, the JSON request helpers), and each domain area (`vehicleApi.ts`, `maintenanceApi.ts`, `notificationsApi.ts`, `mapApi.ts`, etc.) builds on it. Real-time updates use `useSignalR.ts` as described above, not polling.

The vehicle store owns `activeVehicle`/`activeVin` (the one car this instance follows) and `effectiveVehicleType` (the user's manual override, else the drivetrain the API detected from the gateway's `hw_version`); views read those rather than indexing into the vehicle list or repeating the override logic. The TypeScript interfaces in `services/` mirror the API's DTOs by hand; the history endpoint returns `TelemetryHistoryPoint` (the chart fields only), not full snapshots.

Dashboard cards are described once, in `frontend/src/cards/registry.ts`: each entry carries the card's icon, whether it is visible by default for a given drivetrain, and whether the current telemetry has anything to show. The card ids, the default layout and the "does this card have data" checks are all derived from that list, so a new card is one entry plus its markup in `DashboardCardContent.vue`. The map's point-of-interest layers work the same way: `composables/poiTileLayer.ts` holds the fetch-by-tile, cache and cluster logic, and charging stations, fuel stations, service areas and speed cameras are four configurations of it.

Every map is Leaflet, and everything drawn on one (markers, clusters, routes, the heatmap) is a
Leaflet layer. The basemap underneath them is not: `composables/useBasemap.ts` adds a MapLibre GL
layer to Leaflet's tile pane, which renders vector tiles themed to match the app and labelled in
the interface language. MapLibre and its tile worker are loaded lazily through
`utils/maplibreLayer.ts` and never reach a page without a map; if that load fails, or the browser
has no WebGL, the same composable falls back to raster tiles, so a map view is never empty.

## Tests

Backend tests (xUnit) and frontend unit tests (Vitest) cover logic in isolation. On top of those,
`frontend/e2e/` holds Playwright smoke tests that run against the demo stack from
`docker-compose.demo.yml`, which is the production frontend image with the real nginx config.

That distinction matters: the Content-Security-Policy is served by nginx, so it does not exist in
the Vite dev server or in jsdom. A change that every other check accepts can still break the
shipped app, which is exactly what happened when the policy began covering `index.html` and
blocked FontAwesome's runtime stylesheet. The smoke tests fail on any policy violation, and they
check the handful of things that only appear in a real browser, such as icon sizing and the
rotated map marker. CI runs them in the `e2e` job; see CONTRIBUTING.md for running them locally.

## Build conventions

Backend projects share `Directory.Build.props` (analyzers on, warnings are errors, `.editorconfig` style rules enforced in the build) and `Directory.Packages.props` (central package versions). `dotnet format GarageStack.slnx` applies the formatting rules; CI verifies them. The frontend is linted by oxlint and ESLint, its stylesheets by Stylelint, and formatted by Prettier, all verified in CI.

Versions are pinned once: package versions in `Directory.Packages.props` (the release workflow reads MinVer's version from there too, so the tag and the stamped assemblies cannot come from different MinVer majors), and pnpm in `frontend/package.json`'s `packageManager` field, which corepack and the CI action both read. Runtime defaults belong to the app rather than to the deployment files: the compose files and the all-in-one entrypoint pass a setting through only when the operator sets one, so numbers like the tyre pressure thresholds exist in exactly one place.
