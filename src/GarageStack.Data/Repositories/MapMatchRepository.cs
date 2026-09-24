using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GarageStack.Data.Repositories;

// logger is optional (DI always supplies one) so tests can construct this with just a DbContext.
public class MapMatchRepository(AppDbContext db, ILogger<MapMatchRepository>? logger = null) : IMapMatchRepository
{
    public async Task<CachedTraceMatch?> GetValidAsync(string provider, string traceHash, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var entry = await db.MapMatchCacheEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Provider == provider && e.TraceHash == traceHash && e.ExpiresAt > now, ct);

        if (entry is null) return null;
        if (entry.Shape is null) return CachedTraceMatch.NotMatched;

        // A row written by an older version, or by hand, could carry indexes that no longer line
        // up with the shape. Treat that as a miss rather than handing a caller a broken mapping.
        var indexes = SafeJson.TryDeserialize<int[]>(
            entry.PointIndexesJson,
            ex => logger?.LogWarning(ex, "Map match cache row {Id} has unreadable point indexes, ignoring it", entry.Id));

        if (indexes is null) return null;

        // Unreadable limits cost the colouring, not the line, so the row is still worth serving.
        var limits = SafeJson.TryDeserialize<int[]>(
            entry.SpeedLimitRunsJson,
            ex => logger?.LogWarning(ex, "Map match cache row {Id} has unreadable speed limits, serving it without them", entry.Id));

        return new CachedTraceMatch(entry.Shape, indexes, entry.MatchedKm, limits ?? []);
    }

    public async Task UpsertAsync(
        string provider, string traceHash,
        CachedTraceMatch match,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        try
        {
            await UpsertAttemptAsync(provider, traceHash, match, ttl, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Two browsers selected the same trip at once and both missed the cache. Retry once:
            // the second attempt finds the now-committed row and updates it instead.
            logger?.LogDebug("Map match cache row for {Provider} was inserted concurrently, retrying as an update", provider);
            db.ChangeTracker.Clear();
            await UpsertAttemptAsync(provider, traceHash, match, ttl, ct);
        }
    }

    private async Task UpsertAttemptAsync(
        string provider, string traceHash,
        CachedTraceMatch match,
        TimeSpan ttl,
        CancellationToken ct)
    {
        var entry = await db.MapMatchCacheEntries
            .FirstOrDefaultAsync(e => e.Provider == provider && e.TraceHash == traceHash, ct);

        if (entry is null)
        {
            entry = new MapMatchCacheEntry { Provider = provider, TraceHash = traceHash };
            db.MapMatchCacheEntries.Add(entry);
        }

        var now = DateTime.UtcNow;
        entry.Shape = match.Shape;
        entry.PointIndexesJson = match.Shape is null ? null : JsonSerializer.Serialize(match.PointIndexes);
        entry.SpeedLimitRunsJson = match.Shape is null || match.SpeedLimitRuns.Count == 0
            ? null
            : JsonSerializer.Serialize(match.SpeedLimitRuns);
        entry.MatchedKm = match.MatchedKm;
        entry.CachedAt = now;
        entry.ExpiresAt = now.Add(ttl);

        await db.SaveChangesAsync(ct);
    }
}
