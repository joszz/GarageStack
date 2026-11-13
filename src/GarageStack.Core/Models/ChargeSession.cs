using System.Linq.Expressions;
using GarageStack.Core.Helpers;

namespace GarageStack.Core.Models;

/// <summary>
/// One charge, rebuilt from the telemetry rows around it. The battery kWh figures are the gateway's
/// drivetrain/soc_kwh as reported: MG's nominal 72.5 kWh pack scaled by the state of charge on
/// anything but a BEV series the gateway knows, so the browser decides whether to show them.
/// The power curve is fetched separately (<see cref="ChargeSessionPowerPoint"/>), only for the
/// session someone opens.
/// </summary>
public sealed record ChargeSession(
    DateTime StartedAt,
    DateTime? EndedAt,
    double? StartSocPercent,
    double? EndSocPercent,
    string? ChargingType,
    double? OdometerKm,
    double? Latitude,
    double? Longitude,
    double? StartBatteryKwh,
    double? EndBatteryKwh,
    double? BatteryEnergyGainedKwh,
    double? MaxPowerKw);

public sealed record ChargeSessionPowerPoint(DateTime RecordedAt, double PowerKw);

public sealed record ChargeSessionReading(
    long Id,
    DateTime RecordedAt,
    DateTime? LastChargeStateAt,
    bool? IsCharging,
    double? EvSocPercent,
    string? ChargingType,
    DateTime? ChargingLastStartAt,
    DateTime? ChargingLastEndAt,
    double? OdometerKm,
    double? Latitude,
    double? Longitude,
    double? HvSocKwh,
    double? HvPower,
    double? ObcPowerSinglePhase,
    double? ObcPowerThreePhase)
{
    public static readonly Expression<Func<TelemetrySnapshot, ChargeSessionReading>> Projection =
        s => new ChargeSessionReading(
            s.Id,
            s.RecordedAt,
            s.LastChargeStateAt,
            s.IsCharging,
            s.EvSocPercent,
            s.ChargingType,
            s.ChargingLastStartAt,
            s.ChargingLastEndAt,
            s.OdometerKm,
            s.Latitude,
            s.Longitude,
            s.HvSocKwh,
            s.HvPower,
            s.ObcPowerSinglePhase,
            s.ObcPowerThreePhase);

    private static readonly Func<TelemetrySnapshot, ChargeSessionReading> Compiled = Projection.Compile();

    public static ChargeSessionReading FromSnapshot(TelemetrySnapshot snapshot) => Compiled(snapshot);

    /// <summary>The charging power this reading shows, in kW, or null when it shows none.</summary>
    public double? PowerKw => ChargerPower.Kw(ObcPowerSinglePhase, ObcPowerThreePhase, HvPower);
}
