using System.Linq.Expressions;

namespace GarageStack.Core.Models;

public sealed record ChargeSession(
    DateTime StartedAt,
    DateTime? EndedAt,
    double? StartSocPercent,
    double? EndSocPercent,
    string? ChargingType,
    double? OdometerKm,
    double? Latitude,
    double? Longitude,
    double? BatteryEnergyGainedKwh,
    double? MaxPowerKw,
    IReadOnlyList<ChargeSessionPowerPoint> PowerPoints);

public sealed record ChargeSessionPowerPoint(DateTime RecordedAt, double PowerKw);

public sealed record ChargeSessionReading(
    long Id,
    DateTime RecordedAt,
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
}
