using System.Net;
using System.Text;
using GarageStack.Api.Services;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// ---------------------------------------------------------------------------
// Test infrastructure
// ---------------------------------------------------------------------------

internal sealed class GeocodeFakeRepository : IGeocodeRepository
{
    private readonly Dictionary<(string Precision, string Language, int CellLat, int CellLng), GeocodeCacheEntry> _entries = [];

    public int UpsertCallCount { get; private set; }
    public TimeSpan? LastTtl { get; private set; }

    public void Seed(string precision, string language, int cellLat, int cellLng, string? city, TimeSpan? validFor = null)
        => _entries[(precision, language, cellLat, cellLng)] = new GeocodeCacheEntry
        {
            Precision = precision,
            Language = language,
            CellLat = cellLat,
            CellLng = cellLng,
            City = city,
            ExpiresAt = DateTime.UtcNow.Add(validFor ?? TimeSpan.FromDays(1)),
        };

    public GeocodeCacheEntry? Find(string precision, string language, int cellLat, int cellLng)
        => _entries.GetValueOrDefault((precision, language, cellLat, cellLng));

    public Task<IReadOnlyList<GeocodeCacheEntry>> GetValidAsync(
        string precision, string language,
        IReadOnlyList<(int CellLat, int CellLng)> cells,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        IReadOnlyList<GeocodeCacheEntry> result = cells
            .Select(c => _entries.GetValueOrDefault((precision, language, c.CellLat, c.CellLng)))
            .Where(e => e is not null && e.ExpiresAt > now)
            .Select(e => e!)
            .ToList();
        return Task.FromResult(result);
    }

    public Task UpsertAsync(
        string precision, string language,
        int cellLat, int cellLng,
        PlaceAddress place,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        UpsertCallCount++;
        LastTtl = ttl;
        _entries[(precision, language, cellLat, cellLng)] = new GeocodeCacheEntry
        {
            Precision = precision,
            Language = language,
            CellLat = cellLat,
            CellLng = cellLng,
            DisplayName = place.DisplayName,
            Road = place.Road,
            HouseNumber = place.HouseNumber,
            City = place.City,
            Postcode = place.Postcode,
            CountryCode = place.CountryCode,
            ExpiresAt = DateTime.UtcNow.Add(ttl),
        };
        return Task.CompletedTask;
    }
}

internal sealed class GeocodeFakeNominatimHandler : HttpMessageHandler
{
    private readonly Func<int, (HttpStatusCode Status, string Body)> _responder;

    public GeocodeFakeNominatimHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        => _responder = _ => (status, body);

    public List<string> RequestUris { get; } = [];
    public int CallCount => RequestUris.Count;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequestUris.Add(request.RequestUri!.ToString());
        var (status, body) = _responder(RequestUris.Count - 1);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class GeocodeServiceTests
{
    private const string ZwolleResponse = """
        {"display_name":"Grote Markt 1, Zwolle, Overijssel, Nederland",
         "address":{"road":"Grote Markt","house_number":"1","city":"Zwolle","postcode":"8011 PK","country_code":"nl"}}
        """;
    private const string VillageResponse = """
        {"display_name":"Wijthmen, Zwolle, Nederland",
         "address":{"village":"Wijthmen","country_code":"nl"}}
        """;
    private const string UnmappableResponse = """{"error":"Unable to geocode"}""";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static NominatimApiClient BuildClient(GeocodeFakeNominatimHandler handler, IConfiguration? configuration = null)
        => new(new PoiFakeHttpClientFactory(new HttpClient(handler)),
            configuration ?? Config(),
            NullLogger<NominatimApiClient>.Instance);

    private static GeocodeService BuildService(GeocodeFakeRepository repo, GeocodeFakeNominatimHandler handler,
        IConfiguration? configuration = null)
        => new(repo, BuildClient(handler, configuration), NullLogger<GeocodeService>.Instance);

    private static List<GeoPoint> Points(params (double Lat, double Lng)[] coords)
        => coords.Select(c => new GeoPoint(c.Lat, c.Lng)).ToList();

    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResolveAsync_CachedCell_DoesNotCallUpstream()
    {
        var repo = new GeocodeFakeRepository();
        var (cellLat, cellLng) = GeocodePrecisionPolicy.CellOf(52.512, 6.092, GeocodePrecisionPolicy.City);
        repo.Seed(GeocodePrecisionPolicy.City, "nl", cellLat, cellLng, "Zwolle");

        var handler = new GeocodeFakeNominatimHandler(ZwolleResponse);
        var svc = BuildService(repo, handler);

        var result = await svc.ResolveAsync(Points((52.512, 6.092)), GeocodePrecisionPolicy.City, "nl",
            TestContext.Current.CancellationToken);

        Assert.Equal(0, handler.CallCount);
        Assert.Equal("Zwolle", result.Results[0]!.City);
        Assert.False(result.HasMore);
        Assert.True(result.Available);
    }

    [Fact]
    public async Task ResolveAsync_CacheMiss_FetchesAndCachesForTheFullTtl()
    {
        var repo = new GeocodeFakeRepository();
        var handler = new GeocodeFakeNominatimHandler(ZwolleResponse);
        var svc = BuildService(repo, handler);

        var result = await svc.ResolveAsync(Points((52.512, 6.092)), GeocodePrecisionPolicy.Address, "nl",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, repo.UpsertCallCount);
        Assert.Equal(GeocodeDefaults.Ttl, repo.LastTtl);

        var place = result.Results[0]!;
        Assert.Equal("Grote Markt", place.Road);
        Assert.Equal("1", place.HouseNumber);
        Assert.Equal("Zwolle", place.City);
        Assert.Equal("8011 PK", place.Postcode);
        Assert.Equal("nl", place.CountryCode);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task ResolveAsync_SettlementTaggedAsVillage_StillYieldsACityName()
    {
        var repo = new GeocodeFakeRepository();
        var handler = new GeocodeFakeNominatimHandler(VillageResponse);
        var svc = BuildService(repo, handler);

        var result = await svc.ResolveAsync(Points((52.49, 6.23)), GeocodePrecisionPolicy.City, "nl",
            TestContext.Current.CancellationToken);

        Assert.Equal("Wijthmen", result.Results[0]!.City);
    }

    [Fact]
    public async Task ResolveAsync_PointsInOneCityCell_ShareASingleLookup()
    {
        var repo = new GeocodeFakeRepository();
        var handler = new GeocodeFakeNominatimHandler(ZwolleResponse);
        var svc = BuildService(repo, handler);

        // Two stops ~100m apart: the same city cell, so one answer serves both.
        var result = await svc.ResolveAsync(Points((52.5120, 6.0920), (52.5127, 6.0925)),
            GeocodePrecisionPolicy.City, "nl", TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal("Zwolle", result.Results[0]!.City);
        Assert.Equal("Zwolle", result.Results[1]!.City);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task ResolveAsync_SendsTheCallersCoordinateAndThePrecisionsZoom()
    {
        var repo = new GeocodeFakeRepository();
        var handler = new GeocodeFakeNominatimHandler(ZwolleResponse);
        var svc = BuildService(repo, handler);

        await svc.ResolveAsync(Points((52.5123456, 6.0987654)), GeocodePrecisionPolicy.Address, "nl",
            TestContext.Current.CancellationToken);

        // The cache key is quantised to a cell, the query is not: upstream sees the real spot.
        Assert.Contains("lat=52.5123456", handler.RequestUris[0]);
        Assert.Contains("lon=6.0987654", handler.RequestUris[0]);
        Assert.Contains("zoom=18", handler.RequestUris[0]);
        Assert.Contains("accept-language=nl", handler.RequestUris[0]);
    }

    [Fact]
    public async Task ResolveAsync_MoreMissesThanTheBudget_StopsAndReportsHasMore()
    {
        var repo = new GeocodeFakeRepository();
        var handler = new GeocodeFakeNominatimHandler(ZwolleResponse);
        var svc = BuildService(repo, handler);

        // Five separate cities: upstream is capped at one request per second, so the request
        // answers with what it managed and leaves the rest for the client to ask again about.
        var result = await svc.ResolveAsync(
            Points((52.1, 5.1), (52.6, 5.6), (53.1, 6.1), (51.6, 4.6), (51.1, 4.1)),
            GeocodePrecisionPolicy.City, "nl", TestContext.Current.CancellationToken);

        Assert.Equal(GeocodeService.MaxUpstreamLookupsPerRequest, handler.CallCount);
        Assert.Equal(GeocodeService.MaxUpstreamLookupsPerRequest, result.Results.Count(r => r is not null));
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task ResolveAsync_UnmappableCoordinate_CachesAnEmptyPlaceBriefly()
    {
        var repo = new GeocodeFakeRepository();
        var handler = new GeocodeFakeNominatimHandler(UnmappableResponse);
        var svc = BuildService(repo, handler);

        var result = await svc.ResolveAsync(Points((0.0, 0.0)), GeocodePrecisionPolicy.City, "en",
            TestContext.Current.CancellationToken);

        // Resolved (so the client stops asking) but nameless, and only cached for the short TTL.
        Assert.NotNull(result.Results[0]);
        Assert.Null(result.Results[0]!.City);
        Assert.Equal(GeocodeDefaults.NegativeTtl, repo.LastTtl);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task ResolveAsync_UpstreamRateLimited_LeavesPointUnresolvedAndCachesNothing()
    {
        var repo = new GeocodeFakeRepository();
        var handler = new GeocodeFakeNominatimHandler("", HttpStatusCode.TooManyRequests);
        var svc = BuildService(repo, handler);

        var result = await svc.ResolveAsync(Points((52.1, 5.1)), GeocodePrecisionPolicy.City, "en",
            TestContext.Current.CancellationToken);

        Assert.Null(result.Results[0]);
        Assert.Equal(0, repo.UpsertCallCount);
        Assert.True(result.HasMore);
        Assert.True(result.Available);
    }

    [Fact]
    public async Task ResolveAsync_GeocodingDisabled_ReportsUnavailableWithoutAskingUpstream()
    {
        var repo = new GeocodeFakeRepository();
        var handler = new GeocodeFakeNominatimHandler(ZwolleResponse);
        var svc = BuildService(repo, handler, Config(("Geocoding:Enabled", "false")));

        var result = await svc.ResolveAsync(Points((52.1, 5.1)), GeocodePrecisionPolicy.City, "en",
            TestContext.Current.CancellationToken);

        Assert.False(result.Available);
        Assert.False(result.HasMore);
        Assert.Null(result.Results[0]);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_SelfHostedBaseUrl_IsUsedInsteadOfThePublicInstance()
    {
        var repo = new GeocodeFakeRepository();
        var handler = new GeocodeFakeNominatimHandler(ZwolleResponse);
        var svc = BuildService(repo, handler, Config(("Geocoding:BaseUrl", "http://nominatim.local:8080/")));

        await svc.ResolveAsync(Points((52.1, 5.1)), GeocodePrecisionPolicy.City, "en",
            TestContext.Current.CancellationToken);

        Assert.StartsWith("http://nominatim.local:8080/reverse?", handler.RequestUris[0]);
    }

    [Fact]
    public void CellOf_AddressPrecision_SeparatesCoordinatesThatShareACityCell()
    {
        var cityA = GeocodePrecisionPolicy.CellOf(52.5120, 6.0920, GeocodePrecisionPolicy.City);
        var cityB = GeocodePrecisionPolicy.CellOf(52.5127, 6.0925, GeocodePrecisionPolicy.City);
        Assert.Equal(cityA, cityB);

        var addressA = GeocodePrecisionPolicy.CellOf(52.5120, 6.0920, GeocodePrecisionPolicy.Address);
        var addressB = GeocodePrecisionPolicy.CellOf(52.5127, 6.0925, GeocodePrecisionPolicy.Address);
        Assert.NotEqual(addressA, addressB);
    }

    [Theory]
    [InlineData("nl", "nl")]
    [InlineData("NL", "nl")]
    [InlineData("en", "en")]
    [InlineData("de", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void NormalizeLanguage_KeepsSupportedCodesAndFallsBackForTheRest(string? requested, string expected)
        => Assert.Equal(expected, GeocodeDefaults.NormalizeLanguage(requested));

    [Fact]
    public void Normalize_RejectsAnythingButTheTwoKnownPrecisions()
    {
        Assert.Equal(GeocodePrecisionPolicy.City, GeocodePrecisionPolicy.Normalize("city"));
        Assert.Equal(GeocodePrecisionPolicy.Address, GeocodePrecisionPolicy.Normalize("address"));
        Assert.Null(GeocodePrecisionPolicy.Normalize("street"));
        Assert.Null(GeocodePrecisionPolicy.Normalize(null));
    }
}
