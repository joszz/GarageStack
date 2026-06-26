using System.Text.Json;
using GarageStack.Api.Services;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class ChargingStationServiceTests
{
    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWithKey(string key) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenChargeMap:ApiKey"] = key })
            .Build();

    private static OcmApiClient BuildOcmClient(string json, string? apiKey = "test-key")
    {
        var handler = new PoiFakeOverpassHandler(json);
        var factory = new PoiFakeHttpClientFactory(new System.Net.Http.HttpClient(handler));
        var config = apiKey is null ? EmptyConfig() : ConfigWithKey(apiKey);
        return new OcmApiClient(factory, config, NullLogger<OcmApiClient>.Instance);
    }

    private static ChargingStationService Build(PoiFakeRepository repo, OcmApiClient ocm)
        => new(repo, ocm, NullLogger<ChargingStationService>.Instance);

    private static PoiItem MakeItem(int id, double lat, double lng, int powerKw, string connType = "CCS")
    {
        var meta = new OcmApiClient.OcmMeta(null, null, null, true, 1,
            [new OcmApiClient.OcmConnectorMeta(connType, powerKw, 1)]);
        return new PoiItem
        {
            Source = "ocm", PoiType = "charging",
            ExternalId = $"ocm/{id}",
            Latitude = lat, Longitude = lng,
            Name = $"Station {id}",
            MetaJson = JsonSerializer.Serialize(meta),
            CellLat = (int)Math.Floor(lat * 2),
            CellLng = (int)Math.Floor(lng * 2),
        };
    }

    private static void SeedAllTiles(PoiFakeRepository repo, double lat, double lng, int distanceKm)
    {
        foreach (var (cl, cg) in TileHelper.ComputeTiles(lat, lng, distanceKm))
            repo.SeedTile("ocm", "charging", cl, cg);
    }

    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStationsAsync_NoApiKey_ReturnsEmptyList()
    {
        var svc = Build(new PoiFakeRepository(), BuildOcmClient("[]", apiKey: null));

        var result = await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStationsAsync_AllTilesCached_DoesNotCallOcm()
    {
        var repo = new PoiFakeRepository();
        SeedAllTiles(repo, 52.37, 4.90, 5);
        repo.SeedItem(MakeItem(1, 52.37, 4.90, 50));

        var svc = Build(repo, BuildOcmClient("[]"));

        var result = await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.Equal(0, repo.UpsertCallCount);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetStationsAsync_SuccessfulResponse_ReturnsMappedDtos()
    {
        var repo = new PoiFakeRepository();
        SeedAllTiles(repo, 52.37, 4.90, 5);
        repo.SeedItem(new PoiItem
        {
            Source = "ocm", PoiType = "charging",
            ExternalId = "ocm/42",
            Latitude = 52.37, Longitude = 4.90,
            Name = "Fast Charger",
            MetaJson = JsonSerializer.Serialize(new OcmApiClient.OcmMeta(
                "Main St 1", "Amsterdam", "ANWB Energie", true, 4,
                [
                    new OcmApiClient.OcmConnectorMeta("CCS (Type 2)", 150, 2),
                    new OcmApiClient.OcmConnectorMeta("CHAdeMO", 50, 1),
                ])),
            CellLat = (int)Math.Floor(52.37 * 2),
            CellLng = (int)Math.Floor(4.90 * 2),
        });

        var svc = Build(repo, BuildOcmClient("[]"));

        var result = await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.Single(result);
        var s = result[0];
        Assert.Equal(42, s.Id);
        Assert.Equal("Fast Charger", s.Title);
        Assert.Equal("Main St 1", s.AddressLine);
        Assert.Equal("Amsterdam", s.Town);
        Assert.Equal("ANWB Energie", s.Operator);
        Assert.True(s.IsOperational);
        Assert.Equal(4, s.NumberOfPoints);
        Assert.Equal(2, s.Connectors.Count);
        Assert.Equal("CCS (Type 2)", s.Connectors[0].Type);
        Assert.Equal(150, s.Connectors[0].PowerKw);
        Assert.Equal(2, s.Connectors[0].Quantity);
    }

    [Fact]
    public async Task GetStationsAsync_MaxPowerFilter_ExcludesHighPowerOnlyStations()
    {
        var repo = new PoiFakeRepository();
        SeedAllTiles(repo, 52.37, 4.90, 5);
        repo.SeedItem(MakeItem(1, 52.37, 4.90, 150)); // exceeds maxPowerKw=100
        repo.SeedItem(MakeItem(2, 52.38, 4.91, 50));  // within range

        var result = await Build(repo, BuildOcmClient("[]"))
            .GetStationsAsync(52.37, 4.90, 5, maxPowerKw: 100);

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    [Fact]
    public async Task GetStationsAsync_MinPowerFilter_ExcludesLowPowerStations()
    {
        var repo = new PoiFakeRepository();
        SeedAllTiles(repo, 52.37, 4.90, 5);
        repo.SeedItem(MakeItem(1, 52.37, 4.90, 22)); // below minPowerKw=50
        repo.SeedItem(MakeItem(2, 52.38, 4.91, 50)); // meets threshold

        var result = await Build(repo, BuildOcmClient("[]"))
            .GetStationsAsync(52.37, 4.90, 5, minPowerKw: 50);

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    [Fact]
    public async Task GetStationsAsync_BothFilters_OnlyIncludesStationsInRange()
    {
        var repo = new PoiFakeRepository();
        SeedAllTiles(repo, 52.37, 4.90, 5);
        repo.SeedItem(MakeItem(1, 52.37, 4.90, 22));  // too low
        repo.SeedItem(MakeItem(2, 52.38, 4.91, 50));  // in range [50,100]
        repo.SeedItem(MakeItem(3, 52.39, 4.92, 150)); // too high

        var result = await Build(repo, BuildOcmClient("[]"))
            .GetStationsAsync(52.37, 4.90, 5, minPowerKw: 50, maxPowerKw: 100);

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }
}
