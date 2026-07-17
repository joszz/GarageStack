using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GarageStack.Data.Repositories;

// logger is optional (DI always supplies one) so existing tests can keep constructing this
// directly with just a DbContext and cache.
public class PoiRepository(AppDbContext db, IMemoryCache cache, ILogger<PoiRepository>? logger = null) : IPoiRepository
{
    // Brand lists (charging network operators, fuel brands) change rarely, so a short cache
    // avoids re-deserializing every PoiItem's MetaJson on every map filter-panel open.
    private static readonly TimeSpan BrandsCacheTtl = TimeSpan.FromMinutes(15);

    // Defensive cap on GetPoisInBoundsAsync. The map endpoint already bounds radiusKm to
    // <= 200km (MapEndpoints.ValidateRadiusKm), and POI density is inherently bounded by
    // real-world business counts (nowhere near TelemetryRepository's per-vehicle row growth),
    // so this is far above any realistic result set - a safety net, not expected to affect
    // normal queries.
    private const int MaxPoisPerBoundsQuery = 10_000;
    // Used both for the on-demand API path (any tile not cached yet) and the Worker's
    // pre-cache path (tiles whose cache has since expired) - "uncached" and "expired or
    // missing" are the same query: anything outside the currently-valid (ExpiresAt > now) set.
    public async Task<IReadOnlyList<(int CellLat, int CellLng)>> GetExpiredOrMissingTilesAsync(
        string source, string poiType,
        IReadOnlyList<(int CellLat, int CellLng)> tiles,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cellLats = tiles.Select(t => t.CellLat).Distinct().ToList();
        var cellLngs = tiles.Select(t => t.CellLng).Distinct().ToList();

        var valid = await db.PoiCacheTiles
            .Where(t => t.Source == source && t.PoiType == poiType
                        && t.ExpiresAt > now
                        && cellLats.Contains(t.CellLat)
                        && cellLngs.Contains(t.CellLng))
            .Select(t => new { t.CellLat, t.CellLng })
            .ToListAsync(ct);

        var validSet = valid.Select(v => (v.CellLat, v.CellLng)).ToHashSet();
        return tiles.Where(t => !validSet.Contains(t)).ToList();
    }

    public async Task UpsertTileAsync(
        string source, string poiType,
        int cellLat, int cellLng,
        IReadOnlyList<PoiItem> items,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        // A concurrent writer (e.g. API on-demand + Worker pre-cache racing on the same tile) can
        // insert overlapping rows between our SELECT and INSERT, which fails the whole batch with a
        // unique-violation and rolls back this transaction. Retry once: the second attempt re-reads
        // the now-committed rows as "existing" and updates them instead of re-inserting, so this
        // request's own items are actually persisted rather than silently dropped.
        try
        {
            await UpsertTileAttemptAsync(source, poiType, cellLat, cellLng, items, ttl, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            db.ChangeTracker.Clear();
            await UpsertTileAttemptAsync(source, poiType, cellLat, cellLng, items, ttl, ct);
        }
    }

    private async Task UpsertTileAttemptAsync(
        string source, string poiType,
        int cellLat, int cellLng,
        IReadOnlyList<PoiItem> items,
        TimeSpan ttl,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        await UpsertItemsAsync(source, poiType, cellLat, cellLng, items, now, ct);
        await PruneStaleItemsAsync(source, poiType, cellLat, cellLng, items, ct);
        await UpsertCacheTileRowAsync(source, poiType, cellLat, cellLng, now, ttl, ct);

        await db.SaveChangesAsync(ct);
    }

    // Elements near tile boundaries appear in two adjacent tile queries but share the same
    // ExternalId; we UPDATE them rather than INSERT to avoid hitting the unique constraint.
    private async Task UpsertItemsAsync(
        string source, string poiType, int cellLat, int cellLng,
        IReadOnlyList<PoiItem> items, DateTime now, CancellationToken ct)
    {
        var newExternalIds = items.Select(i => i.ExternalId).ToHashSet();
        var existingByExternalId = await db.PoiItems
            .Where(p => p.Source == source && p.PoiType == poiType
                        && newExternalIds.Contains(p.ExternalId))
            .ToDictionaryAsync(p => p.ExternalId, ct);

        foreach (var item in items)
        {
            if (existingByExternalId.TryGetValue(item.ExternalId, out var existing))
            {
                existing.Latitude = item.Latitude;
                existing.Longitude = item.Longitude;
                existing.Name = item.Name;
                existing.MetaJson = item.MetaJson;
                existing.CellLat = item.CellLat;
                existing.CellLng = item.CellLng;
                existing.UpdatedAt = now;
            }
            else
            {
                item.CreatedAt = now;
                item.UpdatedAt = now;
                db.PoiItems.Add(item);
            }
        }
    }

    // Removes items that were owned by this tile in a previous fetch but are no longer present.
    private async Task PruneStaleItemsAsync(
        string source, string poiType, int cellLat, int cellLng,
        IReadOnlyList<PoiItem> items, CancellationToken ct)
    {
        var ownedByTile = await db.PoiItems
            .Where(p => p.Source == source && p.PoiType == poiType
                        && p.CellLat == cellLat && p.CellLng == cellLng)
            .ToListAsync(ct);

        var survivingIds = items
            .Where(i => i.CellLat == cellLat && i.CellLng == cellLng)
            .Select(i => i.ExternalId)
            .ToHashSet();
        var toRemove = ownedByTile.Where(p => !survivingIds.Contains(p.ExternalId)).ToList();
        if (toRemove.Count > 0)
            db.PoiItems.RemoveRange(toRemove);
    }

    private async Task UpsertCacheTileRowAsync(
        string source, string poiType, int cellLat, int cellLng,
        DateTime now, TimeSpan ttl, CancellationToken ct)
    {
        var tile = await db.PoiCacheTiles.FirstOrDefaultAsync(
            t => t.Source == source && t.PoiType == poiType
                 && t.CellLat == cellLat && t.CellLng == cellLng, ct);

        if (tile is null)
        {
            tile = new PoiCacheTile
            {
                Source = source,
                PoiType = poiType,
                CellLat = cellLat,
                CellLng = cellLng,
            };
            db.PoiCacheTiles.Add(tile);
        }

        tile.CachedAt = now;
        tile.ExpiresAt = now.Add(ttl);
    }

    public async Task<IReadOnlyList<PoiItem>> GetPoisInBoundsAsync(
        string source, string poiType,
        double minLat, double minLng,
        double maxLat, double maxLng,
        CancellationToken ct = default)
    {
        var pois = await db.PoiItems
            .Where(p => p.Source == source && p.PoiType == poiType
                        && p.Latitude >= minLat && p.Latitude <= maxLat
                        && p.Longitude >= minLng && p.Longitude <= maxLng)
            .AsNoTracking()
            .Take(MaxPoisPerBoundsQuery)
            .ToListAsync(ct);

        if (pois.Count == MaxPoisPerBoundsQuery)
            logger?.LogWarning(
                "GetPoisInBoundsAsync hit the {Cap}-row cap for source={Source} poiType={PoiType} bounds=({MinLat},{MinLng})-({MaxLat},{MaxLng})",
                MaxPoisPerBoundsQuery, source, poiType, minLat, minLng, maxLat, maxLng);

        return pois;
    }

    public async Task<IReadOnlyList<string>> GetDistinctBrandsAsync(
        string source, string poiType,
        CancellationToken ct = default)
    {
        var cacheKey = $"poi-brands/{source}/{poiType}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var metaJsonList = await db.PoiItems
            .Where(p => p.Source == source && p.PoiType == poiType && p.MetaJson != null)
            .Select(p => p.MetaJson!)
            .AsNoTracking()
            .ToListAsync(ct);

        var brands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in metaJsonList)
        {
            var dict = SafeJson.TryDeserialize<Dictionary<string, string>>(json);
            if (dict is null) continue;
            var brand = dict.GetValueOrDefault("brand") ?? dict.GetValueOrDefault("operator");
            if (!string.IsNullOrWhiteSpace(brand)) brands.Add(brand);
        }

        IReadOnlyList<string> result = [.. brands.Order()];
        cache.Set(cacheKey, result, BrandsCacheTtl);
        return result;
    }
}
