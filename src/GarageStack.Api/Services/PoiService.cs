using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Services;

namespace GarageStack.Api.Services;

public sealed class PoiService(
    IPoiRepository repository,
    OverpassApiClient overpassClient,
    ILogger<PoiService> logger)
{
    public async Task<PoiResult> GetPoisAsync(
        string poiType, double lat, double lng, double radiusKm,
        CancellationToken ct = default)
    {
        // A layer the deployment has switched off answers empty and says so, which is what stops
        // the client asking again; nothing is fetched and nothing already cached is served.
        if (!overpassClient.IsTypeEnabled(poiType))
            return new PoiResult([], HasMore: false, Available: false);

        var tiles = TileHelper.ComputeTiles(lat, lng, radiusKm);
        var uncached = await repository.GetExpiredOrMissingTilesAsync(PoiCacheDefaults.OverpassSource, poiType, tiles, ct);

        // Cap on-demand fetches to the tile closest to the viewport centre. The remaining
        // uncached tiles will be filled by the Worker pre-caching service in the background
        // (and by client-side chain loading). The foreground fetch fails fast and returns null
        // when the gate is busy or rate-limited, so the tile is not cached as empty.
        var toFetch = TileHelper.ClosestTiles(uncached, lat, lng, PoiTileFetcher.DefaultMaxOnDemandTiles);

        var tilesActuallyCached = await PoiTileFetcher.FetchAndCacheAsync(
            toFetch,
            (cellLat, cellLng, token) => overpassClient.FetchAsync(poiType, cellLat, cellLng, token),
            (cellLat, cellLng, items, token) => repository.UpsertTileAsync(
                PoiCacheDefaults.OverpassSource, poiType, cellLat, cellLng, items, PoiCacheDefaults.Ttl, token),
            (ex, cellLat, cellLng) => logger.LogWarning(ex,
                "On-demand Overpass fetch failed for {PoiType} ({CellLat},{CellLng})", poiType, cellLat, cellLng),
            ct);

        // hasMore = uncached tiles still remain after this request (either more exist beyond
        // MaxOnDemandTiles, or a fetch was skipped due to a rate-limit backoff). The client
        // uses this to decide whether to chain another request or to stop.
        bool hasMore = uncached.Count - tilesActuallyCached > 0;

        var (minLat, maxLat, minLng, maxLng) = TileHelper.ComputeBounds(lat, lng, radiusKm);

        var pois = await repository.GetPoisInBoundsAsync(PoiCacheDefaults.OverpassSource, poiType, minLat, minLng, maxLat, maxLng, ct);
        return new PoiResult(pois.Select(MapToDto).ToList(), hasMore);
    }

    public Task<IReadOnlyList<string>> GetBrandsAsync(string poiType, CancellationToken ct = default)
        => repository.GetDistinctBrandsAsync(PoiCacheDefaults.OverpassSource, poiType, ct);

    private PoiItemDto MapToDto(PoiItem p) => new(
        p.ExternalId,
        p.PoiType,
        p.Latitude,
        p.Longitude,
        p.Name,
        SafeJson.TryDeserialize<Dictionary<string, string>>(p.MetaJson,
            ex => logger.LogWarning(ex, "Failed to parse POI meta JSON for {ExternalId}", p.ExternalId)));
}

public sealed record PoiItemDto(
    string ExternalId,
    string PoiType,
    double Latitude,
    double Longitude,
    string? Name,
    Dictionary<string, string>? Tags);

/// <param name="Items">The POIs within the requested bounds that are already cached.</param>
/// <param name="HasMore">Uncached tiles remain in this viewport, so asking again brings more.</param>
/// <param name="Available">
/// False only when the deployment does not serve this layer at all, which tells the client to
/// stop offering it rather than to ask again.
/// </param>
public sealed record PoiResult(IReadOnlyList<PoiItemDto> Items, bool HasMore, bool Available = true);
