using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GarageStack.Api.Endpoints;

public static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        static async Task<(Vehicle? Vehicle, IResult? NotFound)> ResolveVehicleAsync(
            string vin,
            IVehicleRepository vehicles,
            CancellationToken ct)
        {
            var vehicle = await vehicles.GetByVinAsync(vin, ct);
            return vehicle is null ? (null, Results.NotFound()) : (vehicle, null);
        }

        var group = app.MapGroup("/api/vehicles")
            .WithTags("Vehicles")
            .RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var vehicles = await db.Vehicles
                .AsNoTracking()
                .Select(v => new VehicleListItemDto(v.Id, v.Vin, v.Model, v.Series, v.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(vehicles);
        })
        .WithSummary("List all vehicles");

        group.MapGet("/{vin}/config", async (string vin, IVehicleRepository vehicles, CancellationToken ct) =>
        {
            var resolved = await ResolveVehicleAsync(vin, vehicles, ct);
            if (resolved.NotFound is not null) return resolved.NotFound;
            var vehicle = resolved.Vehicle!;
            if (vehicle.ConfigJson is null) return Results.Ok(new Dictionary<string, string>());
            try
            {
                var config = JsonSerializer.Deserialize<Dictionary<string, string>>(vehicle.ConfigJson);
                return Results.Ok(config ?? new Dictionary<string, string>());
            }
            catch
            {
                return Results.Ok(new Dictionary<string, string>());
            }
        })
        .WithSummary("Get vehicle capability config");

        group.MapGet("/{vin}/status", async (string vin, ITelemetryRepository telemetry, IVehicleRepository vehicles, CancellationToken ct) =>
        {
            var resolved = await ResolveVehicleAsync(vin, vehicles, ct);
            if (resolved.NotFound is not null) return resolved.NotFound;
            var vehicle = resolved.Vehicle!;
            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            return snapshot is null ? Results.NoContent() : Results.Ok(snapshot);
        })
        .WithSummary("Get latest telemetry for a vehicle");

        group.MapGet("/{vin}/history", async (
            string vin,
            ITelemetryRepository telemetry,
            IVehicleRepository vehicles,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var resolved = await ResolveVehicleAsync(vin, vehicles, ct);
            if (resolved.NotFound is not null) return resolved.NotFound;
            var vehicle = resolved.Vehicle!;

            var start = from?.UtcDateTime ?? DateTime.UtcNow.AddDays(-7);
            var end = to?.UtcDateTime ?? DateTime.UtcNow;
            if (start >= end)
                return Results.BadRequest(new { error = "from must be before to" });
            var maxRange = TimeSpan.FromDays(90);
            if (end - start > maxRange)
                start = end - maxRange;
            var history = await telemetry.GetHistoryAsync(vehicle.Id, start, end, ct);
            return Results.Ok(history);
        })
        .WithSummary("Get telemetry history for a vehicle");

        group.MapGet("/{vin}/trips/last", async (string vin, IVehicleRepository vehicles, AppDbContext db, CancellationToken ct) =>
        {
            var resolved = await ResolveVehicleAsync(vin, vehicles, ct);
            if (resolved.NotFound is not null) return resolved.NotFound;
            var vehicle = resolved.Vehicle!;

            var row = await db.TelemetrySnapshots
                .Where(s => s.VehicleId == vehicle.Id && s.CurrentJourneyDistance > 0)
                .OrderByDescending(s => s.RecordedAt)
                .Select(s => new { distanceKm = s.CurrentJourneyDistance!.Value, recordedAt = s.RecordedAt })
                .FirstOrDefaultAsync(ct);

            return row is null ? Results.NoContent() : Results.Ok(row);
        })
        .WithSummary("Get last trip summary (distance and timestamp of most recent journey)");

        group.MapGet("/{vin}/trips", async (
            string vin,
            ITelemetryRepository telemetry,
            IVehicleRepository vehicles,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var resolved = await ResolveVehicleAsync(vin, vehicles, ct);
            if (resolved.NotFound is not null) return resolved.NotFound;
            var vehicle = resolved.Vehicle!;

            var start = from?.UtcDateTime ?? DateTime.UtcNow.AddDays(-30);
            var end = to?.UtcDateTime ?? DateTime.UtcNow;
            if (start >= end)
                return Results.BadRequest(new { error = "from must be before to" });
            var maxRange = TimeSpan.FromDays(90);
            if (end - start > maxRange)
                start = end - maxRange;
            var trips = await telemetry.GetTripsAsync(vehicle.Id, start, end, ct);
            return Results.Ok(trips);
        })
        .WithSummary("Get trip history");

        group.MapPost("/{vin}/commands/{command}", async (
            string vin,
            string command,
            JsonElement body,
            IMqttPublisher mqtt,
            IVehicleRepository vehicles,
            CancellationToken ct) =>
        {
            var resolved = await ResolveVehicleAsync(vin, vehicles, ct);
            if (resolved.NotFound is not null) return resolved.NotFound;
            var vehicle = resolved.Vehicle!;
            if (vehicle.SaicUser is null)
                return Results.Problem("SAIC username not yet known for this vehicle");

            if (!body.TryGetProperty("value", out var valueEl))
                return Results.BadRequest(new { error = "Missing 'value' in request body" });

            var value = valueEl.GetString();
            if (string.IsNullOrWhiteSpace(value))
                return Results.BadRequest(new { error = "'value' must be a non-empty string" });

            var topicSuffix = command switch
            {
                "climate"             => "climate/remoteClimateState/set",
                "climate-temperature" => "climate/remoteTemperature/set",
                "rear-defroster"      => "climate/rearWindowDefrosterHeating/set",
                "seat-left"           => "climate/heatedSeatsFrontLeftLevel/set",
                "seat-right"          => "climate/heatedSeatsFrontRightLevel/set",
                "find-my-car"         => "location/findMyCar/set",
                "charge-limit"        => "drivetrain/chargeCurrentLimit/set",
                "scheduled-charging"  => "drivetrain/scheduledCharging/set",
                "lock"                => "doors/locked/set",
                "refresh"             => "refresh/mode/set",
                _ => null
            };

            if (topicSuffix is null)
                return Results.BadRequest(new { error = $"Unknown command '{command}'" });

            var topic = $"saic/{vehicle.SaicUser}/vehicles/{vin}/{topicSuffix}";
            await mqtt.PublishAsync(topic, value, ct);

            return Results.Ok(new { topic, value });
        })
        .WithSummary("Send a command to the vehicle via MQTT");

        group.MapGet("/{vin}/topics", async (string vin, IVehicleRepository vehicles, AppDbContext db, CancellationToken ct) =>
        {
            var resolved = await ResolveVehicleAsync(vin, vehicles, ct);
            if (resolved.NotFound is not null) return resolved.NotFound;
            var vehicle = resolved.Vehicle!;

            var topics = await db.TelemetrySnapshots
                .Where(s => s.VehicleId == vehicle.Id && s.RawTopic != null)
                .GroupBy(s => s.RawTopic!)
                .Select(g => new { topic = g.Key, count = g.Count(), last = g.Max(s => s.RecordedAt) })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            return Results.Ok(topics);
        })
        .WithSummary("Distinct raw MQTT topics seen for a vehicle (one entry per 15-second merge window; topics arriving mid-window are not recorded)");

        var push = app.MapGroup("/api/push")
            .WithTags("Push Notifications")
            .RequireAuthorization();

        push.MapGet("/vapid-public-key", (IConfiguration config) =>
        {
            var key = config["Vapid:PublicKey"];
            return string.IsNullOrWhiteSpace(key)
                ? Results.Problem("VAPID keys not configured")
                : Results.Ok(new { publicKey = key });
        })
        .WithSummary("Get VAPID public key for push subscription");

        push.MapPost("/subscribe", async (PushSubscribeRequest req, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Endpoint) ||
                string.IsNullOrWhiteSpace(req.P256DhKey) ||
                string.IsNullOrWhiteSpace(req.AuthKey))
                return Results.BadRequest(new { error = "Endpoint, P256DhKey and AuthKey are required" });

            var existing = await db.PushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == req.Endpoint, ct);

            if (existing is null)
            {
                db.PushSubscriptions.Add(new PushSubscription
                {
                    Endpoint = req.Endpoint,
                    P256DhKey = req.P256DhKey,
                    AuthKey = req.AuthKey,
                });
            }
            else if (existing.P256DhKey != req.P256DhKey || existing.AuthKey != req.AuthKey)
            {
                existing.P256DhKey = req.P256DhKey;
                existing.AuthKey = req.AuthKey;
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok();
        })
        .WithSummary("Register a browser push subscription");

        push.MapPost("/unsubscribe", async (PushUnsubscribeRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == req.Endpoint, ct);
            if (sub is not null)
            {
                db.PushSubscriptions.Remove(sub);
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok();
        })
        .WithSummary("Remove a push subscription");

        return app;
    }
}

public record PushSubscribeRequest(string Endpoint, string P256DhKey, string AuthKey);
public record PushUnsubscribeRequest(string Endpoint);
public record VehicleListItemDto(int Id, string Vin, string? Model, string? Series, DateTime CreatedAt);
