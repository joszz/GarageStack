using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageStack.Data.Repositories;

public class PoiRepository(AppDbContext db) : IPoiRepository
{
    public async Task<IReadOnlyList<(int CellLat, int CellLng)>> GetUncachedTilesAsync(
        string source, string poiType,
        IReadOnlyList<(int CellLat, int CellLng)> tiles,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cached = await db.PoiCacheTiles
            .Where(t => t.Source == source && t.PoiType == poiType && t.ExpiresAt > now)
            .Select(t => new { t.CellLat, t.CellLng })
            .ToListAsync(ct);

        var cachedSet = cached.Select(c => (c.CellLat, c.CellLng)).ToHashSet();
        return tiles.Where(t => !cachedSet.Contains(t)).ToList();
    }

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
        var now = DateTime.UtcNow;

        // Load any existing items that share an ExternalId with the incoming batch.
        // Elements near tile boundaries appear in two adjacent tile queries but share the same
        // ExternalId; we UPDATE them rather than INSERT to avoid hitting the unique constraint.
        var newExternalIds = items.Select(i => i.ExternalId).ToHashSet();
        var existingByExternalId = await db.PoiItems
            .Where(p => p.Source == source && p.PoiType == poiType
                        && newExternalIds.Contains(p.ExternalId))
            .ToDictionaryAsync(p => p.ExternalId, ct);

        // Load items currently "owned" by this tile so we can prune removed elements.
        var ownedByTile = await db.PoiItems
            .Where(p => p.Source == source && p.PoiType == poiType
                        && p.CellLat == cellLat && p.CellLng == cellLng)
            .ToListAsync(ct);

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

        // Remove items that were owned by this tile but are no longer in the query result.
        var survivingIds = items
            .Where(i => i.CellLat == cellLat && i.CellLng == cellLng)
            .Select(i => i.ExternalId)
            .ToHashSet();
        var toRemove = ownedByTile.Where(p => !survivingIds.Contains(p.ExternalId)).ToList();
        if (toRemove.Count > 0)
            db.PoiItems.RemoveRange(toRemove);

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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // A concurrent writer (e.g. API on-demand + Worker pre-cache racing on the same tile)
            // already committed the same rows. Reset context state and move on — the tile is cached.
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<PoiItem>> GetPoisInBoundsAsync(
        string source, string poiType,
        double minLat, double minLng,
        double maxLat, double maxLng,
        CancellationToken ct = default)
    {
        return await db.PoiItems
            .Where(p => p.Source == source && p.PoiType == poiType
                        && p.Latitude >= minLat && p.Latitude <= maxLat
                        && p.Longitude >= minLng && p.Longitude <= maxLng)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
