namespace GarageStack.Core.Models;

/// <summary>
/// One trace as the matcher answered for it, addressed by a hash of the fixes that were sent.
/// A row whose <see cref="Shape"/> is null is a cached "these fixes snap to nothing", which is
/// what stops the same unmatchable trip being sent upstream every time it is selected.
/// </summary>
public class MapMatchCacheEntry
{
    public int Id { get; set; }

    /// <summary>Hash of the trace this row answers for; see <c>MapMatchService.HashOf</c>.</summary>
    public string TraceHash { get; set; } = string.Empty;

    /// <summary>Which matcher produced it, so switching providers does not serve the old one's answers.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The snapped line as an encoded polyline, or null when nothing matched.</summary>
    public string? Shape { get; set; }

    /// <summary>Per sent fix, its vertex in <see cref="Shape"/>, as a JSON array of integers.</summary>
    public string? PointIndexesJson { get; set; }

    /// <summary>Length of the snapped line, which is the better distance for a trip that matched.</summary>
    public double MatchedKm { get; set; }

    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// A cached answer in the form callers work with: the snapped line and, per fix that was sent,
/// its vertex along that line. <paramref name="Shape"/> is null for a trace that snapped to
/// nothing, and <paramref name="PointIndexes"/> is then empty.
/// </summary>
/// <param name="Shape">The snapped line as an encoded polyline.</param>
/// <param name="PointIndexes">One vertex index per sent fix, in the order they were sent.</param>
/// <param name="MatchedKm">Length of the snapped line in kilometres.</param>
public sealed record CachedTraceMatch(
    string? Shape,
    IReadOnlyList<int> PointIndexes,
    double MatchedKm)
{
    public static readonly CachedTraceMatch NotMatched = new(null, [], 0);
}

/// <summary>Column limits shared between the model configuration and the code that writes the rows.</summary>
public static class MapMatchCacheLimits
{
    /// <summary>A SHA-256 hash in hex.</summary>
    public const int TraceHashLength = 64;

    public const int ProviderMaxLength = 32;
}
