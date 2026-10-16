using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// How each tyre's pressure moved against the other three. Air pressure follows the outside
/// temperature, so all four tyres rise and fall together through the seasons; a tyre that slowly
/// loses air falls away from the other three, and that gap is what is measured here. When one
/// tyre loses 0.2 bar, its change is -0.2 and each other tyre's is about +0.07.
/// </summary>
public static class TyreTrend
{
    /// <summary>The days a trend is measured over, ending today.</summary>
    public const int WindowDays = 14;

    // Days averaged at each end of the window, so one odd reading does not make a trend.
    private const int EdgeDays = 3;

    // Fewer days with all four readings than this and the two ends would overlap.
    private const int MinimumDays = 2 * EdgeDays + 1;

    /// <summary>
    /// The change of each tyre against the other three over <paramref name="days"/> (daily averages,
    /// in any order), in bar. Null when fewer than seven of the days have a reading for every tyre.
    /// </summary>
    public static TyreChanges? Measure(IEnumerable<DailyStat> days)
    {
        var complete = days
            .Where(d => d is { TyrePressureFrontLeft: not null, TyrePressureFrontRight: not null, TyrePressureRearLeft: not null, TyrePressureRearRight: not null })
            .OrderBy(d => d.Date)
            .Select(d => new[] { d.TyrePressureFrontLeft!.Value, d.TyrePressureFrontRight!.Value, d.TyrePressureRearLeft!.Value, d.TyrePressureRearRight!.Value })
            .ToList();
        if (complete.Count < MinimumDays) return null;

        // Each tyre's pressure less the average of the other three, on one day.
        static double Gap(double[] day, int tyre) => day[tyre] - (day.Sum() - day[tyre]) / 3;

        double Change(int tyre) =>
            Math.Round(
                complete.TakeLast(EdgeDays).Average(day => Gap(day, tyre)) -
                complete.Take(EdgeDays).Average(day => Gap(day, tyre)),
                3);

        return new TyreChanges(Change(0), Change(1), Change(2), Change(3), complete.Count);
    }
}

/// <summary>
/// Each tyre's change against the other three, in bar: negative when it lost air the others kept.
/// <paramref name="Days"/> is how many days with all four readings it rests on.
/// </summary>
public sealed record TyreChanges(double FrontLeft, double FrontRight, double RearLeft, double RearRight, int Days)
{
    /// <summary>The tyres that fell at least <paramref name="dropBar"/> behind the others, front left first.</summary>
    public IReadOnlyList<TyrePosition> Losing(double dropBar) =>
        dropBar <= 0
            ? []
            : [.. new[]
            {
                (TyrePosition.FrontLeft, FrontLeft), (TyrePosition.FrontRight, FrontRight),
                (TyrePosition.RearLeft, RearLeft), (TyrePosition.RearRight, RearRight),
            }.Where(t => t.Item2 <= -dropBar).Select(t => t.Item1)];
}

public enum TyrePosition
{
    FrontLeft,
    FrontRight,
    RearLeft,
    RearRight,
}
