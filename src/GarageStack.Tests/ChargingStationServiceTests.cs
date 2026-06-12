using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GarageStack.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// ---------------------------------------------------------------------------
// Test infrastructure
// ---------------------------------------------------------------------------

file sealed class FakeHttpMessageHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    : HttpMessageHandler
{
    public int CallCount { get; private set; }
    public string? LastRequestUrl { get; private set; }
    public HttpRequestHeaders? LastRequestHeaders { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        CallCount++;
        LastRequestUrl = request.RequestUri?.ToString();
        LastRequestHeaders = request.Headers;
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}

file sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class ChargingStationServiceTests
{
    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWithKey(string key) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenChargeMap:ApiKey"] = key })
            .Build();

    private static IMemoryCache NewCache() =>
        new MemoryCache(new MemoryCacheOptions());

    private static ChargingStationService Build(
        IHttpClientFactory factory,
        IConfiguration config,
        IMemoryCache cache) =>
        new(factory, config, cache, NullLogger<ChargingStationService>.Instance);

    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStationsAsync_NoApiKey_ReturnsEmptyList()
    {
        var handler = new FakeHttpMessageHandler("[]");
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var svc = Build(factory, EmptyConfig(), NewCache());

        var result = await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetStationsAsync_CacheHit_DoesNotCallHttpClient()
    {
        const string json = """
            [{"AddressInfo":{"ID":1,"Title":"Station A","Latitude":52.37,"Longitude":4.90},
              "Connections":[{"ConnectionType":{"Title":"CCS"},"PowerKW":50}],
              "OperatorInfo":{"Title":"Operator X"},
              "StatusType":{"IsOperational":true}}]
            """;

        var handler = new FakeHttpMessageHandler(json);
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var cache = NewCache();
        var svc = Build(factory, ConfigWithKey("test-key"), cache);

        var first = await svc.GetStationsAsync(52.37, 4.90, 5);
        var second = await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.Equal(1, handler.CallCount);
        Assert.Single(first);
        Assert.Equal(first[0].Id, second[0].Id);
    }

    [Fact]
    public async Task GetStationsAsync_DifferentFilters_BypassCache()
    {
        const string json = "[{\"AddressInfo\":{\"ID\":1,\"Title\":\"S\",\"Latitude\":52.37,\"Longitude\":4.90}}]";
        var handler = new FakeHttpMessageHandler(json);
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var cache = NewCache();
        var svc = Build(factory, ConfigWithKey("test-key"), cache);

        await svc.GetStationsAsync(52.37, 4.90, 5, minPowerKw: 0);
        await svc.GetStationsAsync(52.37, 4.90, 5, minPowerKw: 50);

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetStationsAsync_AlwaysUsesKmDistanceUnit()
    {
        const string json = "[]";
        var handler = new FakeHttpMessageHandler(json);
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var svc = Build(factory, ConfigWithKey("test-key"), NewCache());

        await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.Contains("distanceunit=KM", handler.LastRequestUrl);
    }

    [Fact]
    public async Task GetStationsAsync_WithMinPowerFilter_IncludesFilterInUrl()
    {
        const string json = "[]";
        var handler = new FakeHttpMessageHandler(json);
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var svc = Build(factory, ConfigWithKey("test-key"), NewCache());

        await svc.GetStationsAsync(52.37, 4.90, 5, minPowerKw: 50);

        Assert.Contains("minpowerkw=50", handler.LastRequestUrl);
    }

    [Fact]
    public async Task GetStationsAsync_SuccessfulResponse_ReturnsMappedDtos()
    {
        const string json = """
            [{"AddressInfo":{"ID":42,"Title":"Fast Charger","AddressLine1":"Main St 1","Town":"Amsterdam",
                             "Latitude":52.3700,"Longitude":4.9000},
              "Connections":[{"ConnectionType":{"Title":"CCS (Type 2)"},"PowerKW":150,"Quantity":2},
                             {"ConnectionType":{"Title":"CHAdeMO"},"PowerKW":50}],
              "OperatorInfo":{"Title":"ANWB Energie"},
              "StatusType":{"IsOperational":true},
              "NumberOfPoints":4}]
            """;

        var factory = new FakeHttpClientFactory(new HttpClient(new FakeHttpMessageHandler(json)));
        var svc = Build(factory, ConfigWithKey("test-key"), NewCache());

        var result = await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.Single(result);
        var station = result[0];
        Assert.Equal(42, station.Id);
        Assert.Equal("Fast Charger", station.Title);
        Assert.Equal("Main St 1", station.AddressLine);
        Assert.Equal("Amsterdam", station.Town);
        Assert.Equal("ANWB Energie", station.Operator);
        Assert.True(station.IsOperational);
        Assert.Equal(4, station.NumberOfPoints);
        Assert.Equal(2, station.Connectors.Count);
        Assert.Equal("CCS (Type 2)", station.Connectors[0].Type);
        Assert.Equal(150, station.Connectors[0].PowerKw);
        Assert.Equal(2, station.Connectors[0].Quantity);
    }

    [Fact]
    public async Task GetStationsAsync_HttpFailure_ReturnsEmptyList()
    {
        var handler = new FakeHttpMessageHandler("{}", HttpStatusCode.InternalServerError);
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var svc = Build(factory, ConfigWithKey("test-key"), NewCache());

        var result = await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStationsAsync_ApiKeyInXApiKeyHeader_NotInUrl()
    {
        const string json = "[]";
        var handler = new FakeHttpMessageHandler(json);
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var svc = Build(factory, ConfigWithKey("my-secret-key"), NewCache());

        await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.DoesNotContain("my-secret-key", handler.LastRequestUrl);
        Assert.True(handler.LastRequestHeaders!.Contains("X-API-Key"));
        Assert.Equal("my-secret-key", handler.LastRequestHeaders.GetValues("X-API-Key").Single());
    }

    [Fact]
    public async Task GetStationsAsync_AlwaysFiltersOperationalStations()
    {
        const string json = "[]";
        var handler = new FakeHttpMessageHandler(json);
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var svc = Build(factory, ConfigWithKey("test-key"), NewCache());

        await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.Contains("statustypeid=50", handler.LastRequestUrl);
    }

    [Fact]
    public async Task GetStationsAsync_AlwaysIncludesClientIdentifier()
    {
        const string json = "[]";
        var handler = new FakeHttpMessageHandler(json);
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var svc = Build(factory, ConfigWithKey("test-key"), NewCache());

        await svc.GetStationsAsync(52.37, 4.90, 5);

        Assert.Contains("client=garagestack", handler.LastRequestUrl);
    }

    [Fact]
    public async Task GetStationsAsync_MaxPowerFilter_ExcludesHighPowerOnlyStations()
    {
        // Station A has only 150 kW connectors (exceeds maxPowerKw=100, no null-power connectors)
        // Station B has a 50 kW connector (within range) — should be included
        const string json = """
            [{"AddressInfo":{"ID":1,"Title":"Fast DC","Latitude":52.37,"Longitude":4.90},
              "Connections":[{"ConnectionType":{"Title":"CCS"},"PowerKW":150}],
              "StatusType":{"IsOperational":true}},
             {"AddressInfo":{"ID":2,"Title":"Mid DC","Latitude":52.38,"Longitude":4.91},
              "Connections":[{"ConnectionType":{"Title":"CCS"},"PowerKW":50}],
              "StatusType":{"IsOperational":true}}]
            """;

        var factory = new FakeHttpClientFactory(new HttpClient(new FakeHttpMessageHandler(json)));
        var svc = Build(factory, ConfigWithKey("test-key"), NewCache());

        var result = await svc.GetStationsAsync(52.37, 4.90, 5, minPowerKw: 0, maxPowerKw: 100);

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    [Fact]
    public async Task GetStationsAsync_MaxPowerFilter_UsesCache_DoesNotCallHttpClientAgain()
    {
        const string json = """
            [{"AddressInfo":{"ID":1,"Title":"Station","Latitude":52.37,"Longitude":4.90},
              "Connections":[{"ConnectionType":{"Title":"CCS"},"PowerKW":50}],
              "StatusType":{"IsOperational":true}}]
            """;

        var handler = new FakeHttpMessageHandler(json);
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var cache = NewCache();
        var svc = Build(factory, ConfigWithKey("test-key"), cache);

        await svc.GetStationsAsync(52.37, 4.90, 5, minPowerKw: 0, maxPowerKw: 0);
        await svc.GetStationsAsync(52.37, 4.90, 5, minPowerKw: 0, maxPowerKw: 100);

        // Both calls share the same cache key (minPowerKw=0) so only one HTTP request is made
        Assert.Equal(1, handler.CallCount);
    }
}
