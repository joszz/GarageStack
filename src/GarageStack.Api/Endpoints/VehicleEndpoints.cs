using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GarageStack.Api.Endpoints;

public static class VehicleEndpoints
{
    /// <summary>
    /// Resolves the {vin} route value to a Vehicle before the handler runs, short-circuiting
    /// with 404 if no such vehicle exists. Shared by every endpoint group that takes a {vin}
    /// route parameter (vehicles, widget, demo). Handlers retrieve the result via
    /// <see cref="GetResolvedVehicle"/>.
    /// </summary>
    public sealed class ResolveVehicleFilter : IEndpointFilter
    {
        private const string VehicleItemKey = "GarageStack.ResolvedVehicle";

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var vin = context.HttpContext.Request.RouteValues["vin"] as string
                ?? throw new InvalidOperationException($"{nameof(ResolveVehicleFilter)} requires a {{vin}} route parameter.");

            var vehicles = context.HttpContext.RequestServices.GetRequiredService<IVehicleRepository>();
            var vehicle = await vehicles.GetByVinAsync(vin, context.HttpContext.RequestAborted);
            if (vehicle is null) return Results.NotFound();

            context.HttpContext.Items[VehicleItemKey] = vehicle;
            return await next(context);
        }

        public static Vehicle GetResolvedVehicle(HttpContext httpContext) =>
            (Vehicle)httpContext.Items[VehicleItemKey]!;
    }

    /// <summary>
    /// Resolves the [start, end) UTC range for a from/to query: defaults the missing bound
    /// (end to now, start to end - defaultSpan) and clamps the span to maxSpan. Returns a 400
    /// IResult if the resulting range is inverted.
    /// </summary>
    private static IResult? TryResolveDateRange(
        DateTimeOffset? from, DateTimeOffset? to, TimeSpan defaultSpan, TimeSpan maxSpan,
        out DateTime start, out DateTime end)
    {
        end = to?.UtcDateTime ?? DateTime.UtcNow;
        start = from?.UtcDateTime ?? end - defaultSpan;

        if (start >= end)
            return Results.BadRequest(new { error = "from must be before to" });

        if (end - start > maxSpan)
            start = end - maxSpan;

        return null;
    }

    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
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

        var vehicleGroup = group.MapGroup("/{vin}").AddEndpointFilter<ResolveVehicleFilter>();

        vehicleGroup.MapGet("/config", (HttpContext httpContext) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);
            if (vehicle.ConfigJson is null) return Results.Ok(new Dictionary<string, string>());
            var config = SafeJson.TryDeserialize<Dictionary<string, string>>(vehicle.ConfigJson)
                ?? new Dictionary<string, string>();
            return Results.Ok(config);
        })
        .WithSummary("Get vehicle capability config");

        vehicleGroup.MapGet("/status", async (HttpContext httpContext, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);
            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            return snapshot is null ? Results.NoContent() : Results.Ok(snapshot);
        })
        .WithSummary("Get latest telemetry for a vehicle");

        vehicleGroup.MapGet("/history", async (
            HttpContext httpContext,
            ITelemetryRepository telemetry,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var rangeError = TryResolveDateRange(from, to, TimeSpan.FromDays(7), TimeSpan.FromDays(90), out var start, out var end);
            if (rangeError is not null) return rangeError;

            var history = await telemetry.GetHistoryAsync(vehicle.Id, start, end, ct);
            return Results.Ok(history);
        })
        .WithSummary("Get telemetry history for a vehicle");

        vehicleGroup.MapGet("/trips/last", async (HttpContext httpContext, AppDbContext db, CancellationToken ct) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var row = await db.TelemetrySnapshots
                .Where(s => s.VehicleId == vehicle.Id && s.CurrentJourneyDistance > 0)
                .OrderByDescending(s => s.RecordedAt)
                .Select(s => new { distanceKm = s.CurrentJourneyDistance!.Value, recordedAt = s.RecordedAt })
                .FirstOrDefaultAsync(ct);

            return row is null ? Results.NoContent() : Results.Ok(row);
        })
        .WithSummary("Get last trip summary (distance and timestamp of most recent journey)");

        vehicleGroup.MapGet("/trips", async (
            HttpContext httpContext,
            ITelemetryRepository telemetry,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var rangeError = TryResolveDateRange(from, to, TimeSpan.FromDays(30), TimeSpan.FromDays(90), out var start, out var end);
            if (rangeError is not null) return rangeError;

            var trips = await telemetry.GetTripsAsync(vehicle.Id, start, end, ct);
            return Results.Ok(trips);
        })
        .WithSummary("Get trip history");

        vehicleGroup.MapPost("/commands/{command}", async (
            HttpContext httpContext,
            string vin,
            string command,
            JsonElement body,
            IMqttPublisher mqtt,
            CancellationToken ct) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);
            if (vehicle.SaicUser is null)
                return Results.Problem("SAIC username not yet known for this vehicle");

            if (!body.TryGetProperty("value", out var valueEl))
                return Results.BadRequest(new { error = "Missing 'value' in request body" });

            if (valueEl.ValueKind != JsonValueKind.String)
                return Results.BadRequest(new { error = "'value' must be a string" });

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

            var validationError = ValidateCommandValue(command, value);

            if (validationError is not null)
                return Results.BadRequest(new { error = validationError });

            var topic = $"saic/{vehicle.SaicUser}/vehicles/{vin}/{topicSuffix}";
            await mqtt.PublishAsync(topic, value, ct);

            return Results.Ok(new { topic, value });
        })
        .WithSummary("Send a command to the vehicle via MQTT");

        vehicleGroup.MapGet("/stats", async (
            HttpContext httpContext,
            DateTimeOffset? from,
            DateTimeOffset? to,
            ITelemetryRepository telemetry,
            CancellationToken ct) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var rangeError = TryResolveDateRange(from, to, TimeSpan.FromDays(30), TimeSpan.FromDays(90), out var start, out var end);
            if (rangeError is not null) return rangeError;

            var stats = await telemetry.GetAggregateStatsAsync(vehicle.Id, start, end, ct);
            return Results.Ok(stats);
        })
        .WithSummary("Get aggregate statistics for a vehicle over a date range");

        vehicleGroup.MapGet("/topics", async (HttpContext httpContext, AppDbContext db, CancellationToken ct) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);

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

    internal static string? ValidateCommandValue(string command, string value) => command switch
    {
        "climate" or "rear-defroster" =>
            value is "on" or "off" ? null : $"'{command}' value must be 'on' or 'off'",
        "climate-temperature" =>
            int.TryParse(value, out var temp) && temp is >= 16 and <= 28
                ? null
                : "'climate-temperature' value must be an integer between 16 and 28",
        "seat-left" or "seat-right" =>
            int.TryParse(value, out var seat) && seat is >= 0 and <= 3
                ? null
                : $"'{command}' value must be an integer between 0 and 3",
        "find-my-car" =>
            value is "activate" or "stop" ? null : "'find-my-car' value must be 'activate' or 'stop'",
        "charge-limit" =>
            int.TryParse(value, out var limit) && limit is >= 1 and <= 100
                ? null
                : "'charge-limit' value must be an integer between 1 and 100",
        "lock" =>
            value is "True" or "False" ? null : "'lock' value must be 'True' or 'False'",
        "refresh" =>
            value == "force" ? null : "'refresh' value must be 'force'",
        // scheduled-charging: the SAIC API expects a JSON blob (mode + start/end time), whose
        // shape isn't validated here; just cap the length forwarded to MQTT.
        _ => value.Length <= 500 ? null : "value is too long"
    };
}

public record PushSubscribeRequest(string Endpoint, string P256DhKey, string AuthKey);
public record PushUnsubscribeRequest(string Endpoint);
public record VehicleListItemDto(int Id, string Vin, string? Model, string? Series, DateTime CreatedAt);
