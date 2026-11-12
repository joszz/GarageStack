using System.Linq.Expressions;

namespace GarageStack.Core.Models;

public sealed record ChargeSession(
    DateTime StartedAt,
    DateTime? EndedAt,
    double? StartSocPercent,
    double? EndSocPercent,
    string? ChargingType);

public sealed record ChargeSessionReading(
    long Id,
    DateTime RecordedAt,
    bool? IsCharging,
    double? EvSocPercent,
    string? ChargingType,
    DateTime? ChargingLastStartAt,
    DateTime? ChargingLastEndAt)
{
    public static readonly Expression<Func<TelemetrySnapshot, ChargeSessionReading>> Projection =
        s => new ChargeSessionReading(
            s.Id,
            s.RecordedAt,
            s.IsCharging,
            s.EvSocPercent,
            s.ChargingType,
            s.ChargingLastStartAt,
            s.ChargingLastEndAt);

    private static readonly Func<TelemetrySnapshot, ChargeSessionReading> Compiled = Projection.Compile();

    public static ChargeSessionReading FromSnapshot(TelemetrySnapshot snapshot) => Compiled(snapshot);
}
