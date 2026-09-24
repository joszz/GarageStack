using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// Caches snapped trips so a trace is only ever sent to the matcher once. Entries are addressed
/// by a hash of the fixes that were sent plus the matcher that answered, never by vehicle or
/// timestamp: two selections of the same trip are the same trace and share one row.
/// </summary>
public interface IMapMatchRepository
{
    /// <summary>The still-valid answer for a trace, or null when it is missing or has expired.</summary>
    Task<CachedTraceMatch?> GetValidAsync(string provider, string traceHash, CancellationToken ct = default);

    /// <summary>
    /// Stores (or refreshes) one trace's answer, valid for <paramref name="ttl"/>. A
    /// <paramref name="match"/> without a shape records that the trace snapped to nothing, which
    /// is worth remembering for a while so the same trip is not sent upstream on every selection.
    /// </summary>
    Task UpsertAsync(
        string provider, string traceHash,
        CachedTraceMatch match,
        TimeSpan ttl,
        CancellationToken ct = default);
}
