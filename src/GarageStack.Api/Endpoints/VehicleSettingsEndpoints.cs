using System.Text.Json;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// One car's own settings: its battery size, tyre bands, alert levels and type override, each
/// falling back to the deployment's default while it is empty.
/// </summary>
public static class VehicleSettingsEndpoints
{
    public const string BodyNotObjectCode = "vehicleSettings.bodyNotObject";
    public const string UnknownFieldCode = "vehicleSettings.unknownField";
    public const string WrongTypeCode = "vehicleSettings.wrongType";

    public static void MapVehicleSettings(this RouteGroupBuilder vehicleGroup, VehicleDefaults defaults)
    {
        vehicleGroup.MapGet("/settings", (HttpContext httpContext) =>
            Results.Ok(ToDto(httpContext.ResolvedVehicle(), defaults)))
        .Produces<VehicleSettingsDto>()
        .WithSummary("Get the vehicle's own settings, with the deployment default each empty one falls back to");

        vehicleGroup.MapPatch("/settings", async (
            HttpContext httpContext,
            JsonElement body,
            IVehicleRepository vehicles,
            CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
            var (updated, refusal) = ApplyPatch(body, vehicle.Settings ?? new VehicleSettings());
            if (refusal is not null) return ApiProblems.BadRequest(refusal);

            if (VehicleSettingsRules.Check(vehicle, updated!, defaults) is { } rule)
                return ApiProblems.BadRequest(rule.Code, rule.Message);

            await vehicles.SetSettingsAsync(vehicle.Id, updated!, ct);
            vehicle.Settings = updated;
            return Results.Ok(ToDto(vehicle, defaults));
        })
        .Produces<VehicleSettingsDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .AddOpenApiOperationTransformer(async (operation, context, ct) =>
        {
            // Read by hand, so a field left out can be told from one cleared with null.
            if (operation.RequestBody?.Content?.TryGetValue("application/json", out var content) == true)
                content.Schema = await context.GetOrCreateSchemaAsync(typeof(VehicleSettingsValues), cancellationToken: ct);
        })
        .WithSummary("Change some of the vehicle's own settings: a field left out stays, null puts it back to the deployment default");
    }

    /// <summary>
    /// The settings after <paramref name="body"/>, a JSON merge patch, is applied to
    /// <paramref name="current"/>, or why it cannot be: the ranges are checked apart, by
    /// <see cref="VehicleSettingsRules.Check"/>.
    /// </summary>
    internal static (VehicleSettings? Updated, ValidationError? Refusal) ApplyPatch(JsonElement body, VehicleSettings current)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return (null, new ValidationError(BodyNotObjectCode, "The body must be a JSON object of the settings to change"));

        var updated = current.Copy();
        foreach (var property in body.EnumerateObject())
        {
            if (property.Name.Equals("vehicleType", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return (null, new ValidationError(WrongTypeCode, "vehicleType must be text or null"));
                updated.VehicleType = property.Value.GetString()?.Trim().ToLowerInvariant();
                continue;
            }

            var rule = VehicleSettingsRules.Numbers.FirstOrDefault(r => r.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase));
            if (rule is null)
                return (null, new ValidationError(UnknownFieldCode, $"{property.Name} is not a vehicle setting"));

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Null:
                    rule.Set(updated, null);
                    break;
                case JsonValueKind.Number:
                    rule.Set(updated, property.Value.GetDouble());
                    break;
                default:
                    return (null, new ValidationError(WrongTypeCode, $"{rule.Name} must be a number or null"));
            }
        }

        return (updated, null);
    }

    internal static VehicleSettingsDto ToDto(Vehicle vehicle, VehicleDefaults defaults)
    {
        var own = vehicle.Settings ?? new VehicleSettings();
        var detected = VehicleTypeHelper.GetVehicleType(vehicle);
        return new VehicleSettingsDto(
            new VehicleSettingsValues(
                own.HvBatteryCapacityKwh, own.TyreLowBar, own.TyreGoodBar, own.TyreHighBar, own.TyreDropBar,
                own.AuxBatteryLowVolt, own.LowFuelPercent, own.LowEvPercent, own.VehicleType),
            new VehicleSettingsValues(
                defaults.Battery.Kwh, defaults.Tyres.LowBar, defaults.Tyres.GoodBar, defaults.Tyres.HighBar,
                defaults.Tyres.DropBar, defaults.Alerts.AuxBatteryLowVolt, defaults.Alerts.LowFuelPercent,
                defaults.Alerts.LowEvPercent, detected == "unknown" ? null : detected));
    }
}

/// <summary>
/// Every per-car setting. In a change, a field left out stays as it is and null clears it; in an
/// answer, null is a value not set.
/// </summary>
/// <param name="HvBatteryCapacityKwh">Usable traction battery size, 0.5 to 200 kWh. Refused on a plain hybrid (hev).</param>
/// <param name="TyreLowBar">Below this a tyre is low, 1 to 5 bar.</param>
/// <param name="TyreGoodBar">From this a tyre is at its placard pressure, 1 to 5 bar, from the low one up.</param>
/// <param name="TyreHighBar">Above this a tyre is too high, 1 to 5 bar, from the good one up.</param>
/// <param name="TyreDropBar">How far one tyre may fall behind the others over two weeks, 0 to 1 bar; 0 switches the slow-puncture alert off.</param>
/// <param name="AuxBatteryLowVolt">12V battery voltage a parked car is warned below, 0 to 15 V; 0 switches the alert off.</param>
/// <param name="LowFuelPercent">Fuel level warned below, 0 to 100 %; 0 switches the alert off.</param>
/// <param name="LowEvPercent">Charge level a plug-in is warned below, 0 to 100 %; 0 switches the alert off.</param>
/// <param name="VehicleType">The drivetrain: hev, phev or bev. As a default, the detected one, or null while none is.</param>
public record VehicleSettingsValues(
    double? HvBatteryCapacityKwh,
    double? TyreLowBar,
    double? TyreGoodBar,
    double? TyreHighBar,
    double? TyreDropBar,
    double? AuxBatteryLowVolt,
    double? LowFuelPercent,
    double? LowEvPercent,
    string? VehicleType);

/// <summary>A car's own settings, and the deployment's default each empty one takes.</summary>
/// <param name="Values">What is set for this car; null where nothing is.</param>
/// <param name="Defaults">The deployment's defaults: its environment variables, and the detected vehicle type.</param>
public record VehicleSettingsDto(VehicleSettingsValues Values, VehicleSettingsValues Defaults);
