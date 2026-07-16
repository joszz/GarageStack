using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// Caches map points-of-interest (charging stations, fuel stations, service areas) in a
/// tile-based grid so repeated map views don't re-hit the upstream API (Open Charge Map /
/// Overpass). <paramref name="source"/> identifies the upstream provider and
/// <paramref name="poiType"/> the category; tiles are addressed by integer
/// <c>(CellLat, CellLng)</c> grid cell, not raw coordinates.
/// </summary>
public interface IPoiRepository
{
    /// <summary>
    /// Of the given <paramref name="tiles"/>, returns the ones with no cached entry or whose
    /// cache has passed its <c>ExpiresAt</c> - i.e. the tiles that need fetching from upstream.
    /// </summary>
    Task<IReadOnlyList<(int CellLat, int CellLng)>> GetExpiredOrMissingTilesAsync(
        string source, string poiType,
        IReadOnlyList<(int CellLat, int CellLng)> tiles,
        CancellationToken ct = default);

    /// <summary>Replaces the cached POIs for one tile with <paramref name="items"/>, valid for <paramref name="ttl"/>.</summary>
    Task UpsertTileAsync(
        string source, string poiType,
        int cellLat, int cellLng,
        IReadOnlyList<PoiItem> items,
        TimeSpan ttl,
        CancellationToken ct = default);

    /// <summary>Returns cached POIs whose coordinates fall within the given lat/lng bounding box.</summary>
    Task<IReadOnlyList<PoiItem>> GetPoisInBoundsAsync(
        string source, string poiType,
        double minLat, double minLng,
        double maxLat, double maxLng,
        CancellationToken ct = default);

    /// <summary>Returns the distinct brand/operator names present in cached POIs' metadata, for the map's filter panel.</summary>
    Task<IReadOnlyList<string>> GetDistinctBrandsAsync(
        string source, string poiType,
        CancellationToken ct = default);
}
