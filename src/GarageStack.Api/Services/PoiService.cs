using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Services;
using Microsoft.Extensions.Logging;

namespace GarageStack.Api.Services;

public sealed class PoiService(
    IPoiRepository repository,
    OverpassApiClient overpassClient,
    ILogger<PoiService> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    public static bool IsPoiTypeAllowed(string poiType, string vehicleType) => poiType switch
    {
        "fuel" => vehicleType is "hev" or "phev",
        "service_area" => true,
        _ => false,
    };

    public static IReadOnlyList<(int CellLat, int CellLng)> ComputeTiles(double lat, double lng, double radiusKm)
        => TileHelper.ComputeTiles(lat, lng, radiusKm);

    public async Task<PoiResult> GetPoisAsync(
        string poiType, double lat, double lng, double radiusKm,
        CancellationToken ct = default)
    {
        var tiles = ComputeTiles(lat, lng, radiusKm);
        var uncached = await repository.GetUncachedTilesAsync("overpass", poiType, tiles, ct);

        // Cap on-demand fetches to the tile closest to the viewport centre. The remaining
        // uncached tiles will be filled by the Worker pre-caching service in the background
        // (and by client-side chain loading). FetchFuelStationsAsync / FetchServiceAreasAsync
        // use the foreground fast-fail path and return null when the gate is busy or rate-limited.
        var toFetch = TileHelper.ClosestTiles(uncached, lat, lng, PoiTileFetcher.DefaultMaxOnDemandTiles);

        var tilesActuallyCached = await PoiTileFetcher.FetchAndCacheAsync(
            toFetch,
            (cellLat, cellLng, token) => poiType switch
            {
                "fuel" => overpassClient.FetchFuelStationsAsync(cellLat, cellLng, token),
                "service_area" => overpassClient.FetchServiceAreasAsync(cellLat, cellLng, token),
                _ => Task.FromResult((IReadOnlyList<PoiItem>?)[]),
            },
            (cellLat, cellLng, items, token) => repository.UpsertTileAsync("overpass", poiType, cellLat, cellLng, items, Ttl, token),
            (ex, cellLat, cellLng) =>
            {
                var safePoiType = poiType.Replace("\r", "").Replace("\n", "");
                logger.LogWarning(ex, "On-demand Overpass fetch failed for {PoiType} ({CellLat},{CellLng})",
                    safePoiType, cellLat, cellLng);
            },
            ct);

        // hasMore = uncached tiles still remain after this request (either more exist beyond
        // MaxOnDemandTiles, or a fetch was skipped due to a rate-limit backoff). The client
        // uses this to decide whether to chain another request or to stop.
        bool hasMore = uncached.Count - tilesActuallyCached > 0;

        var (minLat, maxLat, minLng, maxLng) = TileHelper.ComputeBounds(lat, lng, radiusKm);

        var pois = await repository.GetPoisInBoundsAsync("overpass", poiType, minLat, minLng, maxLat, maxLng, ct);
        return new PoiResult(pois.Select(MapToDto).ToList(), hasMore);
    }

    public Task<IReadOnlyList<string>> GetBrandsAsync(string poiType, CancellationToken ct = default)
        => repository.GetDistinctBrandsAsync("overpass", poiType, ct);

    private static PoiItemDto MapToDto(PoiItem p) => new(
        p.ExternalId,
        p.PoiType,
        p.Latitude,
        p.Longitude,
        p.Name,
        ParseMeta(p.MetaJson));

    private static Dictionary<string, string>? ParseMeta(string? json)
    {
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch { return null; }
    }
}

public sealed record PoiItemDto(
    string ExternalId,
    string PoiType,
    double Latitude,
    double Longitude,
    string? Name,
    Dictionary<string, string>? Tags);

public sealed record PoiResult(IReadOnlyList<PoiItemDto> Items, bool HasMore);
