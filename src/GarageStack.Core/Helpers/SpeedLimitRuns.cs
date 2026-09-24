namespace GarageStack.Core.Helpers;

/// <summary>
/// Speed limits along a snapped line, held as runs instead of one value per segment. A road's
/// limit holds for every vertex of the stretch it covers, so a trip of a few thousand vertices is
/// a few dozen runs: worth encoding both in the cache table and on the wire, for the same reason
/// the line itself travels as a polyline rather than as an array of points.
/// <para>
/// The encoding is a flat array of pairs - segment count, then limit in km/h - covering the
/// segments of a line in order, where a limit of 0 means OSM carries no <c>maxspeed</c> for that
/// stretch. A line of <c>n</c> vertices has <c>n - 1</c> segments, and the counts add up to that.
/// </para>
/// </summary>
public static class SpeedLimitRuns
{
    /// <summary>
    /// Folds one limit per segment into runs. Null and any value at or below zero both mean "not
    /// known", since that is what a matcher answers for a road without a <c>maxspeed</c> tag. A
    /// line with no known limit anywhere encodes as nothing at all: runs of zeros would say the
    /// same as an empty list, only at length and in every cache row.
    /// </summary>
    public static IReadOnlyList<int> Encode(IReadOnlyList<int?> perSegment)
    {
        var runs = new List<int>();
        var count = 0;
        var current = 0;
        var anyKnown = false;

        foreach (var limit in perSegment)
        {
            var value = limit is > 0 ? limit.Value : 0;
            anyKnown |= value > 0;
            if (count > 0 && value == current)
            {
                count++;
                continue;
            }

            if (count > 0)
            {
                runs.Add(count);
                runs.Add(current);
            }

            current = value;
            count = 1;
        }

        if (count > 0)
        {
            runs.Add(count);
            runs.Add(current);
        }

        return anyKnown ? runs : [];
    }

    /// <summary>
    /// Unfolds runs back into one limit per segment, with null where no limit is known. A trailing
    /// count without its limit is dropped: the runs may come from a cache row written by hand or by
    /// an older version, and half a pair says nothing about a stretch of road.
    /// </summary>
    public static IReadOnlyList<int?> Decode(IReadOnlyList<int>? runs)
    {
        if (runs is null || runs.Count < 2) return [];

        var perSegment = new List<int?>();
        for (var i = 0; i + 1 < runs.Count; i += 2)
        {
            var count = runs[i];
            if (count <= 0) continue;

            var limit = runs[i + 1] > 0 ? runs[i + 1] : (int?)null;
            for (var n = 0; n < count; n++) perSegment.Add(limit);
        }

        return perSegment;
    }
}
