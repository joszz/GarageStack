namespace GarageStack.Core.Models;

/// <summary>Why a matcher's answer looks the way it does, which decides whether it is worth caching.</summary>
public enum TraceMatchStatus
{
    /// <summary>The matcher found roads for the trace and <see cref="TraceMatch.Shape"/> holds them.</summary>
    Matched,

    /// <summary>The matcher answered but recognised no road under these fixes; a cacheable answer.</summary>
    NotMatched,

    /// <summary>We could not ask at all (switched off, rate limited, upstream error), so nothing is known yet.</summary>
    Unavailable,
}

/// <summary>
/// One trace as the matcher returned it: the road geometry it was snapped onto, plus where each
/// of the fixes we sent ended up along that geometry. The index is what lets a caller keep
/// per-fix data (a speed reading, say) lined up with the snapped line instead of the raw one;
/// it is null for a fix the matcher could not place.
/// </summary>
/// <param name="Status">Why the answer looks the way it does, and whether it is worth caching.</param>
/// <param name="Shape">The road geometry the trace was snapped onto.</param>
/// <param name="ShapeIndexOfPoint">Per sent fix, its vertex along <paramref name="Shape"/>.</param>
/// <param name="SpeedLimitOfSegment">
/// The limit in km/h on each segment of <paramref name="Shape"/>, so one entry fewer than there
/// are vertices, and null where OSM carries no <c>maxspeed</c> for that stretch. Knowing the
/// limit is what turns a speed reading into "over" or "under".
/// </param>
public sealed record TraceMatch(
    TraceMatchStatus Status,
    IReadOnlyList<GeoCoordinate> Shape,
    IReadOnlyList<int?> ShapeIndexOfPoint,
    IReadOnlyList<int?> SpeedLimitOfSegment)
{
    public static readonly TraceMatch Unavailable = new(TraceMatchStatus.Unavailable, [], [], []);
    public static readonly TraceMatch NotMatched = new(TraceMatchStatus.NotMatched, [], [], []);
}
