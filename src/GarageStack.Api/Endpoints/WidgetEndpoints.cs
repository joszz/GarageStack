using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;

namespace GarageStack.Api.Endpoints;

public static class WidgetEndpoints
{
    public static IEndpointRouteBuilder MapWidgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/widget")
            .WithTags("Widget")
            .AddEndpointFilter(async (ctx, next) =>
            {
                var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var configuredKey = config["Widget:ApiKey"];
                if (string.IsNullOrWhiteSpace(configuredKey))
                    return Results.Problem(
                        "Widget API key is not configured. Set the WIDGET_API_KEY environment variable.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);

                var providedKey = ctx.HttpContext.Request.Headers["X-Widget-Key"].ToString();
                if (!string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
                    return Results.Unauthorized();

                return await next(ctx);
            });

        group.MapGet("/{vin}/status", async (
            string vin,
            IVehicleRepository vehicles,
            ITelemetryRepository telemetry,
            IStringLocalizer<WidgetStrings> localizer,
            CancellationToken ct) =>
        {
            var vehicle = await vehicles.GetByVinAsync(vin, ct);
            if (vehicle is null) return Results.NotFound();

            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            return snapshot is null ? Results.NoContent() : Results.Ok(WidgetStatusDto.FromSnapshot(snapshot, localizer));
        })
        .WithSummary("Get latest vehicle status for Homepage widget (requires X-Widget-Key header)");

        return app;
    }
}

public sealed class WidgetStrings;

public record WidgetStatusDto(
    // Timestamp
    DateTime RecordedAt,
    // Fuel / ICE
    double? FuelLevelPercent,
    double? FuelRangeKm,
    // EV / HV
    double? EvSocPercent,
    string? IsCharging,
    string? ChargerConnected,
    double? MileageSinceLastCharge,
    double? HvSocKwh,
    double? HvTotalCapacityKwh,
    double? HvVoltage,
    double? HvCurrent,
    double? HvPower,
    // Odometer and efficiency
    double? OdometerKm,
    double? MileageOfTheDayKm,
    double? PowerUsageOfDayKwh,
    double? ElectricSharePercent,
    // State
    string? IsLocked,
    string? EngineRunning,
    string? ClimateOn,
    // Doors
    string? DriverDoorOpen,
    string? PassengerDoorOpen,
    string? RearLeftDoorOpen,
    string? RearRightDoorOpen,
    string? TrunkOpen,
    string? BonnetOpen,
    string AnyDoorOpen,
    // Windows
    string? DriverWindowOpen,
    string? PassengerWindowOpen,
    string? RearLeftWindowOpen,
    string? RearRightWindowOpen,
    string? SunRoofOpen,
    string AnyWindowOpen,
    // 12V battery
    double? BatteryVoltage,
    // Temperature
    double? InteriorTemperature,
    double? ExteriorTemperature,
    // Tyre pressures (bar)
    double? TyrePressureFrontLeft,
    double? TyrePressureFrontRight,
    double? TyrePressureRearLeft,
    double? TyrePressureRearRight,
    // Lights
    string? LightsMainBeam,
    string? LightsDippedBeam,
    string? LightsSide,
    // Speed & journey
    double? SpeedKmh,
    double? CurrentJourneyDistanceKm,
    // Online status
    string? IsAvailable,
    DateTime? LastVehicleStateAt,
    DateTime? LastChargeStateAt,
    // Charging session
    int? RemainingChargingTime,
    string? ChargingType,
    string? ChargingCableLock,
    double? ObcPowerSinglePhase,
    double? ObcPowerThreePhase,
    // Battery heating
    string? BatteryHeating,
    string? BatteryHeatingScheduleMode,
    string? BatteryHeatingScheduleStartTime,
    // Location extras
    double? Elevation
)
{
    public static WidgetStatusDto FromSnapshot(TelemetrySnapshot s, IStringLocalizer<WidgetStrings> l)
    {
        static string? Loc(bool? v, string trueKey, string falseKey, IStringLocalizer<WidgetStrings> l) =>
            v is null ? null : l[v.Value ? trueKey : falseKey].Value;

        static string LocBool(bool v, string trueKey, string falseKey, IStringLocalizer<WidgetStrings> l) =>
            l[v ? trueKey : falseKey].Value;

        double? electricShare = s.MileageSinceLastCharge.HasValue && s.MileageOfTheDay is > 0
            ? Math.Min(100, Math.Round(s.MileageSinceLastCharge.Value / s.MileageOfTheDay!.Value * 100))
            : null;

        var anyDoorOpen = s.DriverDoorOpen == true || s.PassengerDoorOpen == true
            || s.RearLeftDoorOpen == true || s.RearRightDoorOpen == true
            || s.TrunkOpen == true || s.BonnetOpen == true;

        var anyWindowOpen = s.DriverWindowOpen == true || s.PassengerWindowOpen == true
            || s.RearLeftWindowOpen == true || s.RearRightWindowOpen == true
            || s.SunRoofOpen == true;

        return new WidgetStatusDto(
            RecordedAt: s.RecordedAt,
            FuelLevelPercent: s.FuelLevelPercent,
            FuelRangeKm: s.FuelRangeKm,
            EvSocPercent: s.EvSocPercent,
            IsCharging: Loc(s.IsCharging, "ChargingYes", "ChargingNo", l),
            ChargerConnected: Loc(s.ChargerConnected, "PluggedIn", "Unplugged", l),
            MileageSinceLastCharge: s.MileageSinceLastCharge,
            HvSocKwh: s.HvSocKwh,
            HvTotalCapacityKwh: s.HvTotalCapacityKwh,
            HvVoltage: s.HvVoltage,
            HvCurrent: s.HvCurrent,
            HvPower: s.HvPower,
            OdometerKm: s.OdometerKm,
            MileageOfTheDayKm: s.MileageOfTheDay,
            PowerUsageOfDayKwh: s.PowerUsageOfDay.HasValue
                ? Math.Round(s.PowerUsageOfDay.Value / 1000.0, 2)
                : null,
            ElectricSharePercent: electricShare,
            IsLocked: Loc(s.IsLocked, "Locked", "Unlocked", l),
            EngineRunning: Loc(s.EngineRunning, "EngineOn", "EngineOff", l),
            ClimateOn: Loc(s.ClimateOn, "On", "Off", l),
            DriverDoorOpen: Loc(s.DriverDoorOpen, "Open", "Closed", l),
            PassengerDoorOpen: Loc(s.PassengerDoorOpen, "Open", "Closed", l),
            RearLeftDoorOpen: Loc(s.RearLeftDoorOpen, "Open", "Closed", l),
            RearRightDoorOpen: Loc(s.RearRightDoorOpen, "Open", "Closed", l),
            TrunkOpen: Loc(s.TrunkOpen, "Open", "Closed", l),
            BonnetOpen: Loc(s.BonnetOpen, "Open", "Closed", l),
            AnyDoorOpen: LocBool(anyDoorOpen, "Open", "Closed", l),
            DriverWindowOpen: Loc(s.DriverWindowOpen, "Open", "Closed", l),
            PassengerWindowOpen: Loc(s.PassengerWindowOpen, "Open", "Closed", l),
            RearLeftWindowOpen: Loc(s.RearLeftWindowOpen, "Open", "Closed", l),
            RearRightWindowOpen: Loc(s.RearRightWindowOpen, "Open", "Closed", l),
            SunRoofOpen: Loc(s.SunRoofOpen, "Open", "Closed", l),
            AnyWindowOpen: LocBool(anyWindowOpen, "Open", "Closed", l),
            BatteryVoltage: s.BatteryVoltage,
            InteriorTemperature: s.InteriorTemperature,
            ExteriorTemperature: s.ExteriorTemperature,
            TyrePressureFrontLeft: s.TyrePressureFrontLeft,
            TyrePressureFrontRight: s.TyrePressureFrontRight,
            TyrePressureRearLeft: s.TyrePressureRearLeft,
            TyrePressureRearRight: s.TyrePressureRearRight,
            LightsMainBeam: Loc(s.LightsMainBeam, "On", "Off", l),
            LightsDippedBeam: Loc(s.LightsDippedBeam, "On", "Off", l),
            LightsSide: Loc(s.LightsSide, "On", "Off", l),
            SpeedKmh: s.Speed,
            CurrentJourneyDistanceKm: s.CurrentJourneyDistance,
            IsAvailable: Loc(s.IsAvailable, "Online", "Offline", l),
            LastVehicleStateAt: s.LastVehicleStateAt,
            LastChargeStateAt: s.LastChargeStateAt,
            RemainingChargingTime: s.RemainingChargingTime,
            ChargingType: s.ChargingType,
            ChargingCableLock: Loc(s.ChargingCableLock, "Locked", "Unlocked", l),
            ObcPowerSinglePhase: s.ObcPowerSinglePhase,
            ObcPowerThreePhase: s.ObcPowerThreePhase,
            BatteryHeating: Loc(s.BatteryHeating, "On", "Off", l),
            BatteryHeatingScheduleMode: s.BatteryHeatingScheduleMode,
            BatteryHeatingScheduleStartTime: s.BatteryHeatingScheduleStartTime,
            Elevation: s.Elevation
        );
    }
}
