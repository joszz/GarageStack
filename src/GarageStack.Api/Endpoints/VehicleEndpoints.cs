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
            .WithTags("Vehicles");

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
            var (vehicle, notFound) = await ResolveVehicleAsync(vin, vehicles, ct);
            if (notFound is not null) return notFound;
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
            var (vehicle, notFound) = await ResolveVehicleAsync(vin, vehicles, ct);
            if (notFound is not null) return notFound;
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
            var (vehicle, notFound) = await ResolveVehicleAsync(vin, vehicles, ct);
            if (notFound is not null) return notFound;

            var start = from?.UtcDateTime ?? DateTime.UtcNow.AddDays(-7);
            var end = to?.UtcDateTime ?? DateTime.UtcNow;
            var history = await telemetry.GetHistoryAsync(vehicle.Id, start, end, ct);
            return Results.Ok(history);
        })
        .WithSummary("Get telemetry history for a vehicle");

        group.MapGet("/{vin}/trips", async (
            string vin,
            ITelemetryRepository telemetry,
            IVehicleRepository vehicles,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var (vehicle, notFound) = await ResolveVehicleAsync(vin, vehicles, ct);
            if (notFound is not null) return notFound;

            var start = from?.UtcDateTime ?? DateTime.UtcNow.AddDays(-30);
            var end = to?.UtcDateTime ?? DateTime.UtcNow;
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
            var (vehicle, notFound) = await ResolveVehicleAsync(vin, vehicles, ct);
            if (notFound is not null) return notFound;
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
            var (vehicle, notFound) = await ResolveVehicleAsync(vin, vehicles, ct);
            if (notFound is not null) return notFound;

            var topics = await db.TelemetrySnapshots
                .Where(s => s.VehicleId == vehicle.Id && s.RawTopic != null)
                .GroupBy(s => s.RawTopic!)
                .Select(g => new { topic = g.Key, count = g.Count(), last = g.Max(s => s.RecordedAt) })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            return Results.Ok(topics);
        })
        .WithSummary("Distinct raw MQTT topics seen for a vehicle");

        var push = app.MapGroup("/api/push")
            .WithTags("Push Notifications");

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
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok();
        })
        .WithSummary("Register a browser push subscription");

        push.MapDelete("/unsubscribe", async (string endpoint, AppDbContext db, CancellationToken ct) =>
        {
            var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
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
public record VehicleListItemDto(int Id, string Vin, string? Model, string? Series, DateTime CreatedAt);
