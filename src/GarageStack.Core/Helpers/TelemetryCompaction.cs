using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Folds old telemetry into fewer rows. The Worker already folds the messages of one poll into a
/// single row over a 15-second window; compaction does the same over a quarter of an hour, once
/// nothing reads the finer detail. Each window keeps its last row, filled in with the newest earlier
/// value of every field that row lacks. "The latest reading at or before a moment" (the odometer,
/// the last position, the current state) therefore gives the same answer at the end of every window
/// as before, and never a reading from later on.
/// </summary>
public static class TelemetryCompaction
{
    /// <summary>
    /// How much time one compacted row covers. A quarter of an hour divides every UTC offset in
    /// use, so a window never spans the car's local midnight, wherever it is driven.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    // Counters that start again from zero: at midnight, after a charge, at a new journey. Folding a
    // reset into the reading before it would lose that reading's total (a day's distance or fuel),
    // so a window is cut in two where one of them drops.
    private static readonly Func<TelemetrySnapshot, double?>[] ResettingCounters =
    [
        s => s.MileageOfTheDay,
        s => s.PowerUsageOfDay,
        s => s.MileageSinceLastCharge,
        s => s.PowerUsageSinceLastCharge,
        s => s.CurrentJourneyDistance,
    ];

    /// <summary>The start of the window <paramref name="at"/> falls in.</summary>
    public static DateTime WindowStart(DateTime at) => new(at.Ticks - at.Ticks % Window.Ticks, at.Kind);

    /// <summary>
    /// Compacts one vehicle's <paramref name="rows"/>. The row each window keeps is filled in place,
    /// so a caller holding tracked entities saves the result as it stands. Rows that are already
    /// compacted come out unchanged.
    /// </summary>
    /// <returns>The rows folded into a kept row, for the caller to remove.</returns>
    public static IReadOnlyList<TelemetrySnapshot> Fold(IEnumerable<TelemetrySnapshot> rows)
    {
        var folded = new List<TelemetrySnapshot>();
        foreach (var group in Groups(rows.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id)))
        {
            var kept = group[^1];
            for (var i = group.Count - 2; i >= 0; i--)
            {
                TelemetryFields.ApplyFirstNonNullFields(kept, group[i]);
                folded.Add(group[i]);
            }
        }

        return folded;
    }

    // Consecutive rows that share a window and see no counter reset between them, oldest first.
    private static IEnumerable<List<TelemetrySnapshot>> Groups(IEnumerable<TelemetrySnapshot> ordered)
    {
        List<TelemetrySnapshot>? group = null;
        var windowStart = DateTime.MinValue;
        var counters = new double?[ResettingCounters.Length];

        foreach (var row in ordered)
        {
            var start = WindowStart(row.RecordedAt);
            if (group is null || start != windowStart || Resets(counters, row))
            {
                if (group is not null) yield return group;
                group = [];
                windowStart = start;
                Array.Clear(counters);
            }

            group.Add(row);
            for (var i = 0; i < ResettingCounters.Length; i++)
                counters[i] = ResettingCounters[i](row) ?? counters[i];
        }

        if (group is not null) yield return group;
    }

    private static bool Resets(double?[] latest, TelemetrySnapshot row)
    {
        for (var i = 0; i < ResettingCounters.Length; i++)
        {
            if (ResettingCounters[i](row) < latest[i]) return true;
        }

        return false;
    }
}
