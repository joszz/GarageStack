using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <param name="Missing">Tiles that were missing or expired before the refresh.</param>
/// <param name="Cached">Of those, the tiles fetched and cached now.</param>
public readonly record struct TileRefresh(int Missing, int Cached)
{
    /// <summary>Whether tiles are still missing: more than the limit, or ones upstream asked to wait for.</summary>
    public bool HasMore => Missing > Cached;
}

/// <summary>
/// The shared "fetch a layer's missing POI tiles from an external API and cache them" pipeline,
/// used by the on-demand map endpoints and the background pre-caching service alike.
/// </summary>
public static class PoiTileFetcher
{
    /// <summary>Default cap on how many tiles are fetched synchronously within a single request.</summary>
    public const int DefaultMaxOnDemandTiles = 1;

    /// <summary>
    /// Brings one layer's cache up to date around a point: of the tiles within
    /// <paramref name="radiusKm"/>, those missing or expired are fetched from upstream and cached,
    /// the closest first and at most <paramref name="maxTiles"/> of them when a limit is given. A
    /// tile the fetch returns null for (upstream asked to wait) is left for later; a failing tile
    /// goes to <paramref name="onError"/> and does not stop the others. The on-demand map
    /// endpoints and the Worker's pre-caching all go through here.
    /// </summary>
    public static async Task<TileRefresh> RefreshAsync(
        IPoiRepository repository,
        string source,
        string poiType,
        double lat,
        double lng,
        double radiusKm,
        int? maxTiles,
        Func<int, int, CancellationToken, Task<IReadOnlyList<PoiItem>?>> fetch,
        Action<Exception, int, int> onError,
        CancellationToken ct)
    {
        var tiles = TileHelper.ComputeTiles(lat, lng, radiusKm);
        var missing = await repository.GetExpiredOrMissingTilesAsync(source, poiType, tiles, ct);
        var toFetch = maxTiles is { } max ? TileHelper.ClosestTiles(missing, lat, lng, max) : missing;

        var cached = 0;
        foreach (var (cellLat, cellLng) in toFetch)
        {
            try
            {
                var items = await fetch(cellLat, cellLng, ct);
                if (items is null) continue;

                await repository.UpsertTileAsync(source, poiType, cellLat, cellLng, items, PoiCacheDefaults.Ttl, ct);
                cached++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                onError(ex, cellLat, cellLng);
            }
        }

        return new TileRefresh(missing.Count, cached);
    }
}
