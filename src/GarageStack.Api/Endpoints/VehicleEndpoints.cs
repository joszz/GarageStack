using System.Text.Json;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

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
        .WithSummary("Get chart history for a vehicle (only the fields the statistics charts use)");

        vehicleGroup.MapGet("/trips/last", async (HttpContext httpContext, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);
            var summary = await telemetry.GetLastTripSummaryAsync(vehicle.Id, ct);
            return summary is null ? Results.NoContent() : Results.Ok(summary);
        })
        .WithSummary("Get last trip summary (distance and timestamp of most recent journey)");

        vehicleGroup.MapGet("/trips", async (
            HttpContext httpContext,
            ITripRepository trips,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var rangeError = TryResolveDateRange(from, to, TimeSpan.FromDays(30), TimeSpan.FromDays(90), out var start, out var end);
            if (rangeError is not null) return rangeError;

            return Results.Ok(await trips.GetTripsAsync(vehicle.Id, start, end, ct));
        })
        .WithSummary("Get trip history (saved trips, then the ones not saved yet, including the trip being driven)");

        vehicleGroup.MapPost("/commands/{command}", async (
            HttpContext httpContext,
            string command,
            JsonElement body,
            IMqttPublisher mqtt,
            VehicleCommandGate commandGate,
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

            var commandTopic = VehicleCommands.TopicFor(command);
            if (commandTopic is null)
                return Results.BadRequest(new { error = $"Unknown command '{command}'" });

            var validationError = ValidateCommandValue(command, value);

            if (validationError is not null)
                return Results.BadRequest(new { error = validationError });

            // The resolved vehicle's VIN, not the raw route value, so the topic always matches
            // the row the filter found.
            var topic = $"saic/{vehicle.SaicUser}/vehicles/{vehicle.Vin}/{commandTopic}/set";
            await commandGate.RunAsync(vehicle.Vin, commandTopic, () => mqtt.PublishAsync(topic, value, ct), ct);

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

        vehicleGroup.MapGet("/topics", async (HttpContext httpContext, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = ResolveVehicleFilter.GetResolvedVehicle(httpContext);
            var topics = await telemetry.GetRawTopicStatsAsync(vehicle.Id, ct);
            return Results.Ok(topics);
        })
        .WithSummary("Distinct raw MQTT topics seen for a vehicle (one entry per 15-second merge window; topics arriving mid-window are not recorded)");

        return app;
    }

    private static readonly HashSet<string> ChargeCurrentLimits =
        new(["6A", "8A", "16A", "MAX"], StringComparer.OrdinalIgnoreCase);

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
        // The gateway maps this onto its ChargeCurrentLimitCode enum, which only knows these
        // four values (it upper-cases the payload first, so any casing is accepted here).
        "charge-limit" =>
            ChargeCurrentLimits.Contains(value)
                ? null
                : $"'charge-limit' value must be one of {string.Join(", ", ChargeCurrentLimits)}",
        "lock" =>
            value is "True" or "False" ? null : "'lock' value must be 'True' or 'False'",
        "refresh" =>
            value == "force" ? null : "'refresh' value must be 'force'",
        // scheduled-charging: the SAIC API expects a JSON blob (mode + start/end time), whose
        // shape isn't validated here; just cap the length forwarded to MQTT.
        _ => value.Length <= 500 ? null : "value is too long"
    };
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
