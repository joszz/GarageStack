using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Shared "fetch a batch of POI tiles from an external API and upsert the results" pipeline,
/// used by the on-demand map endpoints and the background pre-caching service alike.
/// </summary>
public static class PoiTileFetcher
{
    /// <summary>Default cap on how many tiles are fetched synchronously within a single request.</summary>
    public const int DefaultMaxOnDemandTiles = 1;

    /// <summary>
    /// Fetches and upserts each tile in <paramref name="tiles"/> in turn. A failure on one tile is reported
    /// via <paramref name="onError"/> and does not stop the remaining tiles from being processed.
    /// Returns the number of tiles that were successfully fetched and cached.
    /// </summary>
    public static async Task<int> FetchAndCacheAsync(
        IEnumerable<(int CellLat, int CellLng)> tiles,
        Func<int, int, CancellationToken, Task<IReadOnlyList<PoiItem>?>> fetch,
        Func<int, int, IReadOnlyList<PoiItem>, CancellationToken, Task> upsert,
        Action<Exception, int, int> onError,
        CancellationToken ct)
    {
        var succeeded = 0;
        foreach (var (cellLat, cellLng) in tiles)
        {
            try
            {
                var items = await fetch(cellLat, cellLng, ct);
                if (items is not null)
                {
                    await upsert(cellLat, cellLng, items, ct);
                    succeeded++;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                onError(ex, cellLat, cellLng);
            }
        }
        return succeeded;
    }
}
