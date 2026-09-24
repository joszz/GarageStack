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
public sealed record TraceMatch(
    TraceMatchStatus Status,
    IReadOnlyList<GeoCoordinate> Shape,
    IReadOnlyList<int?> ShapeIndexOfPoint)
{
    public static readonly TraceMatch Unavailable = new(TraceMatchStatus.Unavailable, [], []);
    public static readonly TraceMatch NotMatched = new(TraceMatchStatus.NotMatched, [], []);
}
