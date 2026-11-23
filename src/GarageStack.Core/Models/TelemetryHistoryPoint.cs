using System.Linq.Expressions;

namespace GarageStack.Core.Models;

/// <summary>
/// The slice of a <see cref="TelemetrySnapshot"/> the statistics read. The daily statistics are
/// built from these instead of full snapshots: a long period holds thousands of rows, and every
/// field the statistics ignore would otherwise be loaded from the database for nothing. The
/// repository reads this type's field list to decide which rows are worth returning, so that
/// filter follows any change made here. The AddChartIndex migration's partial index lists the
/// same fields in raw SQL; ChartIndexTests fails when the two drift apart. A field in
/// <see cref="ReadAlongOnly"/> is read from the rows the others select but selects none itself.
/// </summary>
public sealed record TelemetryHistoryPoint(
    DateTime RecordedAt,
    double? FuelLevelPercent,
    double? EvSocPercent,
    double? PowerUsageOfDay,
    double? BatteryVoltage,
    bool? ClimateOn,
    bool? IsCharging,
    double? TyrePressureFrontLeft,
    double? TyrePressureFrontRight,
    double? TyrePressureRearLeft,
    double? TyrePressureRearRight,
    double? MileageOfTheDay,
    double? MileageSinceLastCharge,
    double? HvSocKwh,
    double? HvTotalCapacityKwh,
    double? PowerUsageSinceLastCharge,
    double? ExteriorTemperature)
{
    /// <summary>
    /// Fields read from the rows the other fields select, without selecting a row themselves, so
    /// they need not be in the filter or the partial index. The outside temperature comes in the
    /// car's status poll with the 12 V battery's voltage and the tyre pressures, which the Worker
    /// folds into one row, so every reading of it lands in a row the statistics read anyway. Adding
    /// it to the filter would only change the index on the largest table for nothing.
    /// </summary>
    public static readonly IReadOnlySet<string> ReadAlongOnly = new HashSet<string> { nameof(ExteriorTemperature) };

    /// <summary>
    /// One projection shared by the EF query (as an expression tree) and the in-memory demo
    /// repository (compiled), so the field list exists exactly once.
    /// </summary>
    public static readonly Expression<Func<TelemetrySnapshot, TelemetryHistoryPoint>> Projection =
        s => new TelemetryHistoryPoint(
            s.RecordedAt,
            s.FuelLevelPercent,
            s.EvSocPercent,
            s.PowerUsageOfDay,
            s.BatteryVoltage,
            s.ClimateOn,
            s.IsCharging,
            s.TyrePressureFrontLeft,
            s.TyrePressureFrontRight,
            s.TyrePressureRearLeft,
            s.TyrePressureRearRight,
            s.MileageOfTheDay,
            s.MileageSinceLastCharge,
            s.HvSocKwh,
            s.HvTotalCapacityKwh,
            s.PowerUsageSinceLastCharge,
            s.ExteriorTemperature);

    private static readonly Func<TelemetrySnapshot, TelemetryHistoryPoint> Compiled = Projection.Compile();

    public static TelemetryHistoryPoint FromSnapshot(TelemetrySnapshot snapshot) => Compiled(snapshot);
}
