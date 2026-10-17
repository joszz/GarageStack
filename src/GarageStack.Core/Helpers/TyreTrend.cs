using System.Text.Json.Serialization;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// How each tyre's pressure moved against the other three. Air pressure follows the outside
/// temperature, so all four tyres rise and fall together through the seasons; a tyre that slowly
/// loses air falls away from the other three, and that gap is what is measured here. When one
/// tyre loses 0.2 bar, its change is -0.2 and each other tyre's is about +0.07.
/// The tyre's own pressure has to have fallen too: topping up three tyres and leaving the fourth,
/// which was already right, leaves the fourth behind the others without it losing anything.
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

        double Change(Func<double[], double> measure) =>
            Math.Round(complete.TakeLast(EdgeDays).Average(measure) - complete.Take(EdgeDays).Average(measure), 3);

        return new TyreChanges(
            Change(day => Gap(day, 0)), Change(day => Gap(day, 1)), Change(day => Gap(day, 2)), Change(day => Gap(day, 3)),
            complete.Count)
        {
            Own = [Change(day => day[0]), Change(day => day[1]), Change(day => day[2]), Change(day => day[3])],
        };
    }
}

/// <summary>
/// Each tyre's change against the other three, in bar: negative when it lost air the others kept.
/// <paramref name="Days"/> is how many days with all four readings it rests on.
/// </summary>
public sealed record TyreChanges(double FrontLeft, double FrontRight, double RearLeft, double RearRight, int Days)
{
    /// <summary>
    /// Each tyre's own change over the same days, front left first, in bar. Not sent: the dialog
    /// shows the gaps, and the API tells it which tyres are losing air.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<double> Own { get; init; } = [];

    /// <summary>
    /// The tyres losing air, front left first: at least <paramref name="dropBar"/> behind the
    /// others, and down by at least half that themselves. Half, because warmer days over the two
    /// weeks lift a leaking tyre's own pressure along with the others'.
    /// </summary>
    public IReadOnlyList<TyrePosition> Losing(double dropBar)
    {
        if (dropBar <= 0) return [];
        var gaps = new[] { FrontLeft, FrontRight, RearLeft, RearRight };
        return [.. Enum.GetValues<TyrePosition>()
            .Where(p => gaps[(int)p] <= -dropBar && (Own.Count == 0 || Own[(int)p] <= -dropBar / 2))];
    }
}

public enum TyrePosition
{
    FrontLeft,
    FrontRight,
    RearLeft,
    RearRight,
}
