using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

// The one pipeline behind the map's on-demand POI layers and the Worker's pre-caching.
public class PoiTileFetcherTests
{
    private const double Lat = 52.37;
    private const double Lng = 4.89;

    private static Task<IReadOnlyList<PoiItem>?> Nothing(int cellLat, int cellLng, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PoiItem>?>([]);

    [Fact]
    public async Task OnDemand_FetchesOnlyTheTileClosestToThePoint()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new PoiFakeRepository();
        var fetched = new List<(int, int)>();

        var refresh = await PoiTileFetcher.RefreshAsync(
            repo, "src", "fuel", Lat, Lng, radiusKm: 50, maxTiles: 1,
            (cellLat, cellLng, token) => { fetched.Add((cellLat, cellLng)); return Nothing(cellLat, cellLng, token); },
            (_, _, _) => { }, ct);

        Assert.Equal([TileHelper.CellOf(Lat, Lng)], fetched);
        Assert.Equal(1, refresh.Cached);
        Assert.True(refresh.HasMore);
    }

    [Fact]
    public async Task WithoutALimit_EveryMissingTileIsFetched_AndCachedOnesAreLeftAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new PoiFakeRepository();
        var (cellLat, cellLng) = TileHelper.CellOf(Lat, Lng);
        repo.SeedTile("src", "fuel", cellLat, cellLng);
        var all = TileHelper.ComputeTiles(Lat, Lng, 50).Count;

        var refresh = await PoiTileFetcher.RefreshAsync(
            repo, "src", "fuel", Lat, Lng, radiusKm: 50, maxTiles: null, Nothing, (_, _, _) => { }, ct);

        Assert.Equal(all - 1, refresh.Missing);
        Assert.Equal(all - 1, refresh.Cached);
        Assert.False(refresh.HasMore);
    }

    // Upstream asking to wait (null) and upstream failing both leave the tile for a later pass,
    // and neither stops the rest.
    [Fact]
    public async Task ASkippedOrFailingTile_IsLeftForLater_WithoutStoppingTheOthers()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new PoiFakeRepository();
        var errors = new List<(int, int)>();
        var call = 0;

        var refresh = await PoiTileFetcher.RefreshAsync(
            repo, "src", "fuel", Lat, Lng, radiusKm: 50, maxTiles: null,
            (cellLat, cellLng, token) => ++call switch
            {
                1 => Task.FromResult<IReadOnlyList<PoiItem>?>(null),
                2 => throw new HttpRequestException("upstream down"),
                _ => Nothing(cellLat, cellLng, token),
            },
            (_, cellLat, cellLng) => errors.Add((cellLat, cellLng)),
            ct);

        Assert.Single(errors);
        Assert.Equal(refresh.Missing - 2, refresh.Cached);
        Assert.Equal(refresh.Cached, repo.UpsertCallCount);
        Assert.True(refresh.HasMore);
    }
}
