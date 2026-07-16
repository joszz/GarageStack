# Architecture

This is an overview of how GarageStack's services fit together, for contributors who need
the map before their first PR. For end-user setup, see [README.md](README.md); for local dev
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
- **Mosquitto** is the MQTT broker all telemetry and commands flow through. Requires auth; not exposed to the LAN by default.
- **Worker** (`GarageStack.Worker`) subscribes to MQTT and writes telemetry to Postgres. Runs on its own, no inbound HTTP.
- **Api** (`GarageStack.Api`) serves the REST API and SignalR hub the frontend talks to, and separately publishes outbound MQTT messages for remote commands (lock, climate, etc.).
- **Postgres** is the only datastore. The Api also uses it as a pub/sub channel (`pg_notify`/`LISTEN`) to learn about writes the Worker makes, so it can push live updates without polling the DB.
- **frontend** is a Vue 3 SPA built and served by nginx; the only service with a public port by default.

## Data flow: a telemetry update reaching the browser

1. `saic-mqtt-gateway` polls the SAIC cloud and publishes one MQTT message per changed field to a topic like `saic/{user}/vehicles/{vin}/drivetrain/soc`.
2. `MqttConsumerService` ([src/GarageStack.Worker/Mqtt/MqttConsumerService.cs](src/GarageStack.Worker/Mqtt/MqttConsumerService.cs)) is subscribed to `saic/#`. It extracts the VIN and subtopic, maps the payload onto a `TelemetrySnapshot` field via `TelemetryMapper`, and writes it to Postgres. A single poll cycle produces several MQTT messages in quick succession, so messages arriving within a 15s window are merged into one row instead of one row per field (see the comments on `MergeOrAddTelemetryAsync` in that file for why this needs a per-vehicle lock).
3. Each write calls `pg_notify('telemetry_updated', vehicleId)`.
4. `TelemetryNotificationService` ([src/GarageStack.Api/Services/TelemetryNotificationService.cs](src/GarageStack.Api/Services/TelemetryNotificationService.cs)), a background service in the Api process, holds a `LISTEN telemetry_updated` connection. On notification it debounces briefly (to coalesce a poll cycle's several writes into one update), re-reads the merged latest snapshot, and broadcasts it over SignalR to browsers subscribed to that vehicle's group.
5. The frontend's `useSignalR` composable ([frontend/src/composables/useSignalR.ts](frontend/src/composables/useSignalR.ts)) receives the `telemetryUpdated` event and updates the UI. There is no polling fallback - if the SignalR connection drops, the dashboard goes stale until it reconnects.

The same `pg_notify`/`LISTEN`/SignalR pattern also carries `notification_created` (push/in-app alerts) and `trip_completed` events.

## Sending a command to the car (the reverse path)

The frontend calls `POST /api/vehicles/{vin}/commands/{command}` on the Api, which publishes an MQTT message via `MqttPublisher` ([src/GarageStack.Api/Services/MqttPublisher.cs](src/GarageStack.Api/Services/MqttPublisher.cs)) - a separate MQTT client from the Worker's, since the Api only ever publishes and the Worker only ever consumes. `saic-mqtt-gateway` picks the message up and relays it to the SAIC cloud API. `VehicleCommandGate` ([src/GarageStack.Api/VehicleCommandGate.cs](src/GarageStack.Api/VehicleCommandGate.cs)) serializes commands per VIN server-side, since the real gateway only processes one command at a time.

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
- Map POI tiles (charging stations, fuel stations, service areas) are cached in Postgres itself
  (`PoiCacheTile`/`PoiItem`), not in memory, since that data needs to survive process restarts
  and be queried by bounding box - a job a plain in-memory cache isn't suited for anyway.

If GarageStack ever needs to run more than one Api/Worker instance, revisit this: per-vehicle
in-memory state (this cache, `VehicleCommandGate`, the Overpass/OCM rate limiters) would all
need to move to something shared.

## Database

Code-first EF Core migrations live in `src/GarageStack.Data/Migrations/`. `Program.cs` runs `db.Database.MigrateAsync()` on Api startup in normal operation; in `DEMO_MODE` it calls `EnsureCreated()` and seeds fake data instead, bypassing a real Postgres server entirely.

## Frontend

REST calls go through `frontend/src/services/` - `apiCore.ts` centralizes the `fetch` wrapper (cookie-based auth, shared 401 handling), and each domain area (`vehicleApi.ts`, `maintenanceApi.ts`, `notificationsApi.ts`, `mapApi.ts`, etc.) builds on it. Real-time updates use `useSignalR.ts` as described above, not polling.
