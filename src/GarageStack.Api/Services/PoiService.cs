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

    public async Task<IReadOnlyList<PoiItemDto>> GetPoisAsync(
        string poiType, double lat, double lng, double radiusKm,
        CancellationToken ct = default)
    {
        var tiles = ComputeTiles(lat, lng, radiusKm);
        var uncached = await repository.GetUncachedTilesAsync("overpass", poiType, tiles, ct);

        // Cap on-demand fetches to the tiles closest to the viewport centre. The remaining
        // uncached tiles will be filled by the Worker pre-caching service in the background.
        // This keeps the API response well under the server-side request timeout even on a
        // cold cache (no 429 backoff concerns here — FetchFuelStationsAsync / FetchServiceAreasAsync
        // use the foreground fast-fail path and return [] immediately when the gate is busy).
        const int MaxOnDemandTiles = 1;
        var centerCellLat = (int)Math.Floor(lat * 2);
        var centerCellLng = (int)Math.Floor(lng * 2);
        var toFetch = uncached
            .OrderBy(t => Math.Abs(t.CellLat - centerCellLat) + Math.Abs(t.CellLng - centerCellLng))
            .Take(MaxOnDemandTiles)
            .ToList();

        foreach (var (cellLat, cellLng) in toFetch)
        {
            try
            {
                var items = poiType switch
                {
                    "fuel" => await overpassClient.FetchFuelStationsAsync(cellLat, cellLng, ct),
                    "service_area" => await overpassClient.FetchServiceAreasAsync(cellLat, cellLng, ct),
                    _ => (IReadOnlyList<PoiItem>)[],
                };
                await repository.UpsertTileAsync("overpass", poiType, cellLat, cellLng, items, Ttl, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "On-demand Overpass fetch failed for {PoiType} ({CellLat},{CellLng})",
                    poiType, cellLat, cellLng);
            }
        }

        var lngFactor = 111.0 * Math.Cos(lat * Math.PI / 180.0);
        var minLat = lat - radiusKm / 111.0;
        var maxLat = lat + radiusKm / 111.0;
        var minLng = lngFactor > 0 ? lng - radiusKm / lngFactor : lng - 1.0;
        var maxLng = lngFactor > 0 ? lng + radiusKm / lngFactor : lng + 1.0;

        var pois = await repository.GetPoisInBoundsAsync("overpass", poiType, minLat, minLng, maxLat, maxLng, ct);
        return pois.Select(MapToDto).ToList();
    }

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
