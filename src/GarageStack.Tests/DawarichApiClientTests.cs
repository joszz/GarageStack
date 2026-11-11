using System.Net;
using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Data.Services;
using Microsoft.Extensions.Configuration;

namespace GarageStack.Tests;

public class DawarichApiClientTests
{
    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Response(string body, int pages = 1)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        response.Headers.Add("X-Total-Pages", pages.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }

    private static readonly DateTime From = DateTimeOffset.FromUnixTimeSeconds(1767261600).UtcDateTime;
    private const string Point = """[{"latitude":"52","longitude":5,"timestamp":1767261600,"accuracy":7,"velocity":12.5,"tracker_id":"fake-phone"}]""";
    private static DawarichApiClient Client(HttpClient http) =>
        new(http, new() { Url = "https://dawarich.invalid/base", ApiKey = "fake-key" }, TimeProvider.System);

    [Fact]
    public async Task PagesMapsSecondsAndUsesOnlyBearerAuthentication()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(req =>
        {
            requests++;
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("fake-key", req.Headers.Authorization.Parameter);
            Assert.DoesNotContain("fake-key", req.RequestUri!.ToString());
            Assert.Contains($"page={requests}", req.RequestUri.Query);
            Assert.Contains("/base/api/v1/points", req.RequestUri.AbsolutePath);
            return Response(Point, 2);
        }));
        var fixes = await Client(http).GetPointsAsync(From, From.AddHours(1), TestContext.Current.CancellationToken);
        Assert.Equal(2, requests);
        Assert.Equal(From, fixes[0].RecordedAt);
        // Dawarich stores metres per second: 12.5 m/s is 45 km/h.
        Assert.Equal(45, fixes[0].SpeedKmh!.Value, 6);
        Assert.Equal(7, fixes[0].AccuracyM);
        Assert.Equal("fake-phone", fixes[0].TrackerId);
    }

    [Fact]
    public async Task BadKeyIsDistinctFromUnreachable()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.Unauthorized)));
        Assert.Equal(DawarichHealth.BadKey, await Client(http).CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefusesOversizedResponse()
    {
        using var http = new HttpClient(new Handler(_ => Response(new string(' ', DawarichApiClient.MaxResponseBytes + 1))));
        await Assert.ThrowsAsync<DawarichException>(() => Client(http).GetPointsAsync(From, From.AddHours(1), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task InvalidPayloadFailsExplicitly(string body)
    {
        using var http = new HttpClient(new Handler(_ => Response(body)));
        var failure = await Assert.ThrowsAsync<DawarichException>(() =>
            Client(http).GetPointsAsync(From, From.AddHours(1), TestContext.Current.CancellationToken));
        Assert.False(failure.Retryable);
    }

    [Fact]
    public async Task StreamingCapDoesNotDependOnContentLength()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(new byte[DawarichApiClient.MaxResponseBytes + 1])),
        }));
        var failure = await Assert.ThrowsAsync<DawarichException>(() =>
            Client(http).GetPointsAsync(From, From.AddHours(1), TestContext.Current.CancellationToken));
        Assert.Contains("size limit", failure.Message);
    }

    [Fact]
    public async Task EmptyTrackerUsesTopicAndMissingIdentifiersAreNotDiscovered()
    {
        using var http = new HttpClient(new Handler(_ => Response(
            """[{"latitude":52,"longitude":5,"timestamp":1767261600,"tracker_id":"","topic":"fake/phone"},{"latitude":52,"longitude":5,"timestamp":1767261600}]""")));
        var trackers = await Client(http).GetTrackersAsync(From, TestContext.Current.CancellationToken);
        Assert.Equal("fake/phone", Assert.Single(trackers).TrackerId);
    }

    [Theory]
    [InlineData("\"-1\"")]
    [InlineData("112")]
    public async Task ImpossibleSpeedsAreDropped(string velocity)
    {
        using var http = new HttpClient(new Handler(_ => Response(Point.Replace("12.5", velocity))));
        var fixes = await Client(http).GetPointsAsync(From, From.AddHours(1), TestContext.Current.CancellationToken);
        Assert.Null(Assert.Single(fixes).SpeedKmh);
    }

    [Fact]
    public async Task DiscoveryReadsOnlyTheNewestPagesOfABusyMonth()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(req =>
        {
            requests.Add(req.RequestUri!.Query);
            return Response(Point, 500);
        }));
        var trackers = await Client(http).GetTrackersAsync(From.AddDays(-30), TestContext.Current.CancellationToken);
        Assert.Equal("fake-phone", Assert.Single(trackers).TrackerId);
        Assert.Equal(DawarichApiClient.DiscoveryPages, requests.Count);
        Assert.All(requests, query => Assert.Contains("order=desc", query));
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedToAnOutage()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        using var http = new HttpClient(new Handler(_ => Response(Point)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(http).GetPointsAsync(From, From.AddHours(1), cancellation.Token));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(-1)]
    public async Task RefusesInvalidOrExcessivePagination(int pages)
    {
        using var http = new HttpClient(new Handler(_ => Response(Point, pages)));
        await Assert.ThrowsAsync<DawarichException>(() => Client(http).GetPointsAsync(From, From.AddHours(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefusesMillisecondsInsteadOfInventingATime()
    {
        using var http = new HttpClient(new Handler(_ => Response(Point.Replace("1767261600", "1767261600000"))));
        await Assert.ThrowsAsync<DawarichException>(() => Client(http).GetPointsAsync(From, From.AddHours(1), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("file:///data")]
    [InlineData("https://user:password@example.invalid")]
    [InlineData("https://example.invalid?key=fake")]
    public void RefusesUnsafeBaseUrls(string url)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dawarich:Url"] = url }).Build();
        Assert.Throws<InvalidOperationException>(() => DawarichOptions.From(config));
    }
}
