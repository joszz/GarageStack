using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

public interface IPoiRepository
{
    Task<IReadOnlyList<(int CellLat, int CellLng)>> GetExpiredOrMissingTilesAsync(
        string source, string poiType,
        IReadOnlyList<(int CellLat, int CellLng)> tiles,
        CancellationToken ct = default);

    Task UpsertTileAsync(
        string source, string poiType,
        int cellLat, int cellLng,
        IReadOnlyList<PoiItem> items,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<IReadOnlyList<PoiItem>> GetPoisInBoundsAsync(
        string source, string poiType,
        double minLat, double minLng,
        double maxLat, double maxLng,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetDistinctBrandsAsync(
        string source, string poiType,
        CancellationToken ct = default);
}
