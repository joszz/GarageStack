using System.Net;
using System.Text;
using GarageStack.Api.Services;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// ---------------------------------------------------------------------------
// Test infrastructure
// ---------------------------------------------------------------------------

internal sealed class PoiFakeRepository : IPoiRepository
{
    private readonly List<PoiItem> _items = [];
    private readonly HashSet<(string source, string type, int clat, int clng)> _tiles = [];
    public int UpsertCallCount { get; private set; }

    public void SeedTile(string source, string poiType, int cellLat, int cellLng)
        => _tiles.Add((source, poiType, cellLat, cellLng));

    public void SeedItem(PoiItem item) => _items.Add(item);

    public Task<IReadOnlyList<(int CellLat, int CellLng)>> GetUncachedTilesAsync(
        string source, string poiType,
        IReadOnlyList<(int CellLat, int CellLng)> tiles,
        CancellationToken ct = default)
    {
        IReadOnlyList<(int CellLat, int CellLng)> result = tiles
            .Where(t => !_tiles.Contains((source, poiType, t.CellLat, t.CellLng)))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<(int CellLat, int CellLng)>> GetExpiredOrMissingTilesAsync(
        string source, string poiType,
        IReadOnlyList<(int CellLat, int CellLng)> tiles,
        CancellationToken ct = default)
        => GetUncachedTilesAsync(source, poiType, tiles, ct);

    public Task UpsertTileAsync(
        string source, string poiType,
        int cellLat, int cellLng,
        IReadOnlyList<PoiItem> items,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        UpsertCallCount++;
        _tiles.Add((source, poiType, cellLat, cellLng));
        _items.AddRange(items);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PoiItem>> GetPoisInBoundsAsync(
        string source, string poiType,
        double minLat, double minLng,
        double maxLat, double maxLng,
        CancellationToken ct = default)
    {
        IReadOnlyList<PoiItem> result = _items
            .Where(p => p.Source == source && p.PoiType == poiType
                        && p.Latitude >= minLat && p.Latitude <= maxLat
                        && p.Longitude >= minLng && p.Longitude <= maxLng)
            .ToList();
        return Task.FromResult(result);
    }
}

internal sealed class PoiFakeOverpassHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }
}

internal sealed class PoiFakeHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class PoiServiceTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    private static OverpassApiClient BuildOverpassClient(PoiFakeOverpassHandler handler)
    {
        var client = new HttpClient(handler);
        var factory = new PoiFakeHttpClientFactory(client);
        return new OverpassApiClient(factory, EmptyConfig(), NullLogger<OverpassApiClient>.Instance);
    }

    private static PoiService BuildPoiService(PoiFakeRepository repo, OverpassApiClient overpass)
        => new(repo, overpass, NullLogger<PoiService>.Instance);

    private const string EmptyOverpassResponse = """{"version":0.6,"elements":[]}""";
    private const string OneNodeResponse = """
        {"version":0.6,"elements":[
            {"type":"node","id":1234,"lat":52.3,"lon":4.9,"tags":{"name":"Shell","brand":"Shell"}}
        ]}
        """;

    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPoisAsync_AllTilesCached_DoesNotCallOverpass()
    {
        var repo = new PoiFakeRepository();
        var handler = new PoiFakeOverpassHandler(EmptyOverpassResponse);
        var svc = BuildPoiService(repo, BuildOverpassClient(handler));

        // Pre-seed every tile that ComputeTiles would return for this request
        var tiles = PoiService.ComputeTiles(52.0, 5.0, 10.0);
        foreach (var (clat, clng) in tiles)
            repo.SeedTile("overpass", "fuel", clat, clng);

        await svc.GetPoisAsync("fuel", 52.0, 5.0, 10.0);

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, repo.UpsertCallCount);
    }

    [Fact]
    public async Task GetPoisAsync_CacheMiss_FetchesFromOverpassAndCaches()
    {
        var repo = new PoiFakeRepository();
        var handler = new PoiFakeOverpassHandler(OneNodeResponse);
        var svc = BuildPoiService(repo, BuildOverpassClient(handler));

        var result = await svc.GetPoisAsync("fuel", 52.3, 4.9, 5.0);

        Assert.True(handler.CallCount > 0);
        Assert.True(repo.UpsertCallCount > 0);
        Assert.Contains(result.Items, p => p.Name == "Shell");
    }

    [Fact]
    public async Task GetPoisAsync_OverpassFailure_ContinuesAndReturnsSeededItems()
    {
        var repo = new PoiFakeRepository();
        var tiles = PoiService.ComputeTiles(52.3, 4.9, 5.0).ToList();
        var firstTile = tiles[0];

        // Seed one tile + one item to verify partial return still works
        repo.SeedTile("overpass", "fuel", firstTile.CellLat, firstTile.CellLng);
        repo.SeedItem(new PoiItem
        {
            Source = "overpass", PoiType = "fuel",
            ExternalId = "node/999", Latitude = 52.31, Longitude = 4.91,
            Name = "BP", CellLat = firstTile.CellLat, CellLng = firstTile.CellLng,
        });

        var handler = new PoiFakeOverpassHandler("", HttpStatusCode.ServiceUnavailable);
        var svc = BuildPoiService(repo, BuildOverpassClient(handler));

        // Should not throw even with HTTP errors for uncached tiles
        var result = await svc.GetPoisAsync("fuel", 52.3, 4.9, 5.0);

        Assert.Contains(result.Items, p => p.Name == "BP");
    }

    [Theory]
    [InlineData("fuel", "bev", false)]
    [InlineData("fuel", "unknown", false)]
    [InlineData("fuel", "hev", true)]
    [InlineData("fuel", "phev", true)]
    [InlineData("service_area", "bev", true)]
    [InlineData("service_area", "hev", true)]
    [InlineData("service_area", "phev", true)]
    [InlineData("service_area", "unknown", true)]
    public void IsPoiTypeAllowed_ReturnsExpectedResult(string poiType, string vehicleType, bool expected)
    {
        Assert.Equal(expected, PoiService.IsPoiTypeAllowed(poiType, vehicleType));
    }

    [Fact]
    public void ComputeTiles_KnownPoint_ReturnsContainingCell()
    {
        // lat=52.3, lng=4.9 → cellLat=floor(52.3*2)=104, cellLng=floor(4.9*2)=9
        var tiles = PoiService.ComputeTiles(52.3, 4.9, 0.1);

        Assert.Contains((104, 9), tiles);
    }

    [Fact]
    public void ComputeTiles_LargeRadius_ReturnsMultipleCells()
    {
        // 100km radius should span several 0.5° cells
        var tiles = PoiService.ComputeTiles(52.3, 4.9, 100.0);

        Assert.True(tiles.Count > 4);
    }
}
