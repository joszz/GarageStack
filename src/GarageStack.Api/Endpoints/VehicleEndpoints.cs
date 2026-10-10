using System.Text.Json;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;

namespace GarageStack.Api.Endpoints;

public static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles")
            .WithTags("Vehicles")
            .RequireAuthorization();

        // Resolved once here rather than taken as a handler parameter: it is deployment
        // configuration, not request input.
        var hvBatteryCapacity = app.ServiceProvider.GetRequiredService<HvBatteryCapacity>();

        group.MapGet("/", async (IVehicleRepository vehicles, CancellationToken ct) =>
        {
            var all = await vehicles.GetAllAsync(ct);
            return Results.Ok(all.Select(v => new VehicleListItemDto(
                v.Id, v.Vin, v.Model, v.Series, v.CreatedAt, VehicleTypeHelper.GetVehicleType(v),
                hvBatteryCapacity.Kwh)));
        })
        .WithSummary("List all vehicles");

        group.MapGet("/tyre-pressure-thresholds", (TyrePressureThresholds thresholds) => Results.Ok(thresholds))
        .WithSummary("Get configured tyre pressure colour-coding thresholds");

        var vehicleGroup = group.MapGroup("/{vin}").AddEndpointFilter<ResolveVehicleFilter>();

        vehicleGroup.MapGet("/config", (HttpContext httpContext) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
            if (vehicle.ConfigJson is null) return Results.Ok(new Dictionary<string, string>());
            var config = SafeJson.TryDeserialize<Dictionary<string, string>>(vehicle.ConfigJson)
                ?? new Dictionary<string, string>();
            return Results.Ok(config);
        })
        .WithSummary("Get vehicle capability config");

        vehicleGroup.MapGet("/status", async (HttpContext httpContext, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
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
            var vehicle = httpContext.ResolvedVehicle();

            var rangeError = DateRange.TryResolve(from, to, TimeSpan.FromDays(7), TimeSpan.FromDays(90), out var start, out var end);
            if (rangeError is not null) return rangeError;

            var history = await telemetry.GetHistoryAsync(vehicle.Id, start, end, ct);
            return Results.Ok(history);
        })
        .WithSummary("Get chart history for a vehicle (only the fields the statistics charts use)");

        vehicleGroup.MapGet("/trips/last", async (HttpContext httpContext, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
            var summary = await telemetry.GetLastTripSummaryAsync(vehicle.Id, ct);
            return summary is null ? Results.NoContent() : Results.Ok(summary);
        })
        .WithSummary("Get last trip summary (distance and timestamp of most recent journey)");

        vehicleGroup.MapPost("/commands/{command}", async (
            HttpContext httpContext,
            string command,
            JsonElement body,
            VehicleCommandSender sender,
            CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
            // Checked before the body, so a car whose account is not known yet answers 409 whatever
            // was sent. The sender checks it again for callers that skip this endpoint.
            if (vehicle.SaicUser is null)
                return ApiProblems.Problem(StatusCodes.Status409Conflict, VehicleCommandSender.AccountUnknownCode,
                    "SAIC username not yet known for this vehicle");

            if (!body.TryGetProperty("value", out var valueEl))
                return ApiProblems.BadRequest("command.valueMissing", "Missing 'value' in request body");

            if (valueEl.ValueKind != JsonValueKind.String)
                return ApiProblems.BadRequest("command.valueNotText", "'value' must be a string");

            var value = valueEl.GetString();
            if (string.IsNullOrWhiteSpace(value))
                return ApiProblems.BadRequest("command.valueEmpty", "'value' must be a non-empty string");

            // The resolved vehicle, not the raw route value, so the topic always matches the row
            // the filter found.
            var outcome = await sender.SendAsync(vehicle, command, value, ct);
            if (outcome.Refusal is { } refusal)
                return ApiProblems.BadRequest(refusal);

            return Results.Ok(new { topic = outcome.Topic, value });
        })
        .WithSummary("Send a command to the vehicle via MQTT");

        vehicleGroup.MapGet("/stats", async (
            HttpContext httpContext,
            DateTimeOffset? from,
            DateTimeOffset? to,
            ITelemetryRepository telemetry,
            CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            var rangeError = DateRange.TryResolve(from, to, TimeSpan.FromDays(30), TimeSpan.FromDays(90), out var start, out var end);
            if (rangeError is not null) return rangeError;

            var stats = await telemetry.GetAggregateStatsAsync(vehicle.Id, start, end, ct);
            return Results.Ok(stats);
        })
        .WithSummary("Get aggregate statistics for a vehicle over a date range");

        vehicleGroup.MapGet("/topics", async (HttpContext httpContext, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
            var topics = await telemetry.GetRawTopicStatsAsync(vehicle.Id, ct);
            return Results.Ok(topics);
        })
        .WithSummary("Distinct raw MQTT topics seen for a vehicle (one entry per 15-second merge window; topics arriving mid-window are not recorded)");

        return app;
    }
}

/// <summary>
/// A vehicle as the list endpoint returns it. <c>VehicleType</c> is the drivetrain detected from
/// the vehicle's reported hardware version (hev, phev, bev, or unknown while nothing has reported
/// one), served here so every client reads the same answer instead of parsing it themselves.
/// <c>HvBatteryCapacityKwh</c> rides along for the same reason: it decides how a client turns a
/// state of charge into kWh, and is null when the deployment has not configured one.
/// </summary>
public record VehicleListItemDto(
    int Id,
    string Vin,
    string? Model,
    string? Series,
    DateTime CreatedAt,
    string VehicleType,
    double? HvBatteryCapacityKwh);
