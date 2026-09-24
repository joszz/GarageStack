using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
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

internal sealed class MapMatchFakeRepository : IMapMatchRepository
{
    private readonly Dictionary<(string Provider, string Hash), (CachedTraceMatch Match, DateTime ExpiresAt)> _entries = [];

    public int UpsertCallCount { get; private set; }
    public TimeSpan? LastTtl { get; private set; }
    public CachedTraceMatch? LastUpsert { get; private set; }

    public void Seed(string provider, string hash, CachedTraceMatch match)
        => _entries[(provider, hash)] = (match, DateTime.UtcNow.AddDays(1));

    /// <summary>The one hash this repository has been asked about, so a test can seed it.</summary>
    public string? LastRequestedHash { get; private set; }

    public Task<CachedTraceMatch?> GetValidAsync(string provider, string traceHash, CancellationToken ct = default)
    {
        LastRequestedHash = traceHash;
        if (!_entries.TryGetValue((provider, traceHash), out var entry) || entry.ExpiresAt <= DateTime.UtcNow)
            return Task.FromResult<CachedTraceMatch?>(null);
        return Task.FromResult<CachedTraceMatch?>(entry.Match);
    }

    public Task UpsertAsync(string provider, string traceHash, CachedTraceMatch match, TimeSpan ttl, CancellationToken ct = default)
    {
        UpsertCallCount++;
        LastTtl = ttl;
        LastUpsert = match;
        _entries[(provider, traceHash)] = (match, DateTime.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }
}

internal sealed class MapMatchFakeValhallaHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);

    public List<string> RequestUris { get; } = [];
    public List<string> RequestBodies { get; } = [];
    public int CallCount => RequestUris.Count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequestUris.Add(request.RequestUri!.ToString());
        RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));

        var (status, body) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.OK, "{}");
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class MapMatchServiceTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static MapMatchService BuildService(
        MapMatchFakeRepository repo, MapMatchFakeValhallaHandler handler, IConfiguration? configuration = null)
        => new(repo,
            new ValhallaApiClient(new PoiFakeHttpClientFactory(new HttpClient(handler)),
                configuration ?? Config(), NullLogger<ValhallaApiClient>.Instance),
            NullLogger<MapMatchService>.Instance);

    /// <summary>A fix <paramref name="km"/> north of the same starting point; longitude never moves.</summary>
    private static GeoPoint Fix(double km) => new(52.0 + km * 0.009, 6.0);

    /// <summary>The fixes a trip would carry: <paramref name="count"/> of them, evenly spaced.</summary>
    private static List<GeoPoint> Trace(int count, double spacingKm = 0.5)
        => [.. Enumerable.Range(0, count).Select(i => Fix(i * spacingKm))];

    /// <summary>
    /// The line a matcher would answer with for such a trace: the same stretch of road, but at the
    /// resolution OSM holds it in rather than one vertex per fix.
    /// </summary>
    private static List<GeoCoordinate> Road(double fromKm, double toKm, int vertices = 40)
        => [.. Enumerable.Range(0, vertices)
            .Select(i => fromKm + (toKm - fromKm) * i / (vertices - 1.0))
            .Select(km => new GeoCoordinate(52.0 + km * 0.009, 6.00002))];

    /// <summary>
    /// A trace_attributes answer holding <paramref name="shape"/> as one edge, with each sent fix
    /// placed at its share along it. A null entry is a fix the matcher could not place, which it
    /// reports with the largest possible edge index rather than by leaving the field out.
    /// </summary>
    private static (HttpStatusCode, string) TraceResponse(IReadOnlyList<GeoCoordinate> shape, params double?[] alongPerFix)
    {
        var encoded = JsonSerializer.Serialize(PolylineCodec.Encode(shape));
        var matched = alongPerFix.Select(along => along is null
            ? """{"edge_index":18446744073709551615}"""
            : $$"""{"edge_index":0,"distance_along_edge":{{along.Value.ToString("0.#####", CultureInfo.InvariantCulture)}}}""");

        return (HttpStatusCode.OK, $$"""
            {"shape":{{encoded}},
             "edges":[{"begin_shape_index":0,"end_shape_index":{{shape.Count - 1}}}],
             "matched_points":[{{string.Join(",", matched)}}]}
            """);
    }

    /// <summary>
    /// The same answer, but with the line split into one edge per entry of
    /// <paramref name="limitPerEdge"/> - a null entry being a road OSM has no <c>maxspeed</c> for,
    /// which the matcher reports by leaving the field out rather than by answering zero. The fixes
    /// are spread evenly along the first edge, since these tests are about the limits rather than
    /// about where the fixes landed.
    /// </summary>
    private static (HttpStatusCode, string) TraceResponseWithLimits(
        IReadOnlyList<GeoCoordinate> shape, int?[] limitPerEdge, int fixCount = 2)
    {
        var encoded = JsonSerializer.Serialize(PolylineCodec.Encode(shape));
        var lastVertex = shape.Count - 1;

        var edges = limitPerEdge.Select((limit, e) =>
        {
            var begin = e * lastVertex / limitPerEdge.Length;
            var end = (e + 1) * lastVertex / limitPerEdge.Length;
            var speedLimit = limit is null ? "" : $$""","speed_limit":{{limit}}""";
            return $$"""{"begin_shape_index":{{begin}},"end_shape_index":{{end}}{{speedLimit}}}""";
        });

        var matched = EvenlyAlong(fixCount).Select(along =>
            $$"""{"edge_index":0,"distance_along_edge":{{along!.Value.ToString("0.#####", CultureInfo.InvariantCulture)}}}""");

        return (HttpStatusCode.OK, $$"""
            {"shape":{{encoded}},
             "edges":[{{string.Join(",", edges)}}],
             "matched_points":[{{string.Join(",", matched)}}]}
            """);
    }

    /// <summary>The share along the road each of <paramref name="count"/> evenly spread fixes sits at.</summary>
    private static double?[] EvenlyAlong(int count)
        => [.. Enumerable.Range(0, count).Select(i => (double?)i / (count - 1))];

    // -----------------------------------------------------------------------

    [Fact]
    public async Task MatchAsync_CachedTrace_DoesNotCallUpstream()
    {
        var repo = new MapMatchFakeRepository();
        var handler = new MapMatchFakeValhallaHandler();
        var svc = BuildService(repo, handler);
        var trace = Trace(4);

        // Ask once to learn the hash this trace is addressed by, then seed it and ask again.
        await svc.MatchAsync(trace, TestContext.Current.CancellationToken);
        var road = Road(0, 1.5);
        repo.Seed(ValhallaApiClient.Provider, repo.LastRequestedHash!,
            new CachedTraceMatch(PolylineCodec.Encode(road), [0, 13, 26, 39], 1.5, [39, 80]));
        var callsBefore = handler.CallCount;

        var result = await svc.MatchAsync(trace, TestContext.Current.CancellationToken);

        Assert.Equal(callsBefore, handler.CallCount);
        Assert.True(result.Matched);
        Assert.Equal(1.5, result.MatchedKm);
        Assert.Equal([0, 13, 26, 39], result.PointIndexes);
    }

    [Fact]
    public async Task MatchAsync_CacheMiss_SnapsTheTraceAndCachesItForTheFullTtl()
    {
        var repo = new MapMatchFakeRepository();
        var road = Road(0, 1.5);
        var handler = new MapMatchFakeValhallaHandler(TraceResponse(road, EvenlyAlong(4)));
        var svc = BuildService(repo, handler);

        var result = await svc.MatchAsync(Trace(4), TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.CallCount);
        Assert.True(result.Available);
        Assert.True(result.Matched);
        Assert.False(result.Pending);

        // The snapped line is what the matcher answered, at its own resolution rather than the
        // four fixes that were sent.
        var shape = PolylineCodec.Decode(result.Shape);
        Assert.Equal(road.Count, shape.Count);
        Assert.Equal(road[0].Lng, shape[0].Lng, 6);
        Assert.Equal(TraceSegmenter.LengthKm(road), result.MatchedKm, 1);

        // One vertex per fix, in order, spread across the line.
        Assert.Equal(4, result.PointIndexes!.Count);
        Assert.Equal(0, result.PointIndexes[0]);
        Assert.Equal(road.Count - 1, result.PointIndexes[^1]);
        Assert.Equal(result.PointIndexes, result.PointIndexes.Order());

        Assert.Equal(1, repo.UpsertCallCount);
        Assert.Equal(MapMatchDefaults.Ttl, repo.LastTtl);
    }

    [Fact]
    public async Task MatchAsync_MatcherRecognisesNoRoad_CachesTheMissBriefly()
    {
        var repo = new MapMatchFakeRepository();
        var handler = new MapMatchFakeValhallaHandler(
            (HttpStatusCode.BadRequest, """{"error_code":171,"error":"No suitable edges near location"}"""));
        var svc = BuildService(repo, handler);

        var result = await svc.MatchAsync(Trace(4), TestContext.Current.CancellationToken);

        Assert.True(result.Available);
        Assert.False(result.Matched);
        Assert.False(result.Pending);
        Assert.Null(result.Shape);
        Assert.Equal(1, repo.UpsertCallCount);
        Assert.Equal(MapMatchDefaults.NegativeTtl, repo.LastTtl);
    }

    [Fact]
    public async Task MatchAsync_UpstreamRateLimited_AsksForARetryAndCachesNothing()
    {
        var repo = new MapMatchFakeRepository();
        var handler = new MapMatchFakeValhallaHandler((HttpStatusCode.TooManyRequests, ""));
        var svc = BuildService(repo, handler);

        var result = await svc.MatchAsync(Trace(4), TestContext.Current.CancellationToken);

        Assert.True(result.Available);
        Assert.False(result.Matched);
        Assert.True(result.Pending);
        Assert.Equal(0, repo.UpsertCallCount);
    }

    [Fact]
    public async Task MatchAsync_MatchingDisabled_ReportsUnavailableWithoutAskingUpstream()
    {
        var repo = new MapMatchFakeRepository();
        var handler = new MapMatchFakeValhallaHandler();
        var svc = BuildService(repo, handler, Config(("MapMatching:Enabled", "false")));

        var result = await svc.MatchAsync(Trace(4), TestContext.Current.CancellationToken);

        Assert.False(result.Available);
        Assert.False(result.Matched);
        Assert.False(result.Pending);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, repo.UpsertCallCount);
    }

    [Fact]
    public async Task MatchAsync_SelfHostedBaseUrl_IsUsedInsteadOfThePublicInstance()
    {
        var repo = new MapMatchFakeRepository();
        var handler = new MapMatchFakeValhallaHandler(TraceResponse(Road(0, 1.5), EvenlyAlong(4)));
        var svc = BuildService(repo, handler, Config(("MapMatching:BaseUrl", "http://valhalla.local:8002/")));

        await svc.MatchAsync(Trace(4), TestContext.Current.CancellationToken);

        Assert.Equal("http://valhalla.local:8002/trace_attributes", handler.RequestUris[0]);
    }

    [Fact]
    public async Task MatchAsync_TraceWithAHole_IsMatchedInPartsAndStitchedBackTogether()
    {
        var repo = new MapMatchFakeRepository();
        var before = Road(0, 1.0, vertices: 20);
        var after = Road(30, 31.0, vertices: 20);
        var handler = new MapMatchFakeValhallaHandler(
            TraceResponse(before, EvenlyAlong(3)),
            TraceResponse(after, EvenlyAlong(3)));
        var svc = BuildService(repo, handler);

        // Three fixes, a thirty-kilometre hole where telemetry stopped, then three more. Upstream
        // refuses a trace containing a jump that wide, so each side goes separately.
        List<GeoPoint> trace = [Fix(0), Fix(0.5), Fix(1.0), Fix(30), Fix(30.5), Fix(31.0)];

        var result = await svc.MatchAsync(trace, TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.CallCount);
        Assert.True(result.Matched);

        // One line holding both snapped stretches; the hole between them stays the straight jump
        // it always was, and every fix still points at a vertex, in order.
        var shape = PolylineCodec.Decode(result.Shape);
        Assert.Equal(before.Count + after.Count, shape.Count);
        Assert.Equal(6, result.PointIndexes!.Count);
        Assert.Equal(result.PointIndexes, result.PointIndexes.Order());
        Assert.Equal(before.Count - 1, result.PointIndexes[2]);
        Assert.Equal(before.Count, result.PointIndexes[3]);
    }

    [Fact]
    public async Task MatchAsync_MatchMuchLongerThanTheFixes_KeepsTheRawTrace()
    {
        var repo = new MapMatchFakeRepository();
        // The matcher answered with a five-kilometre detour for a one-and-a-half kilometre trace:
        // a plausible route, but not the one that was driven.
        var handler = new MapMatchFakeValhallaHandler(TraceResponse(Road(0, 5), EvenlyAlong(4)));
        var svc = BuildService(repo, handler);

        var result = await svc.MatchAsync(Trace(4), TestContext.Current.CancellationToken);

        Assert.False(result.Matched);
        Assert.Null(result.Shape);
        Assert.Equal(MapMatchDefaults.NegativeTtl, repo.LastTtl);
    }

    [Fact]
    public async Task MatchAsync_SparseFixes_AllowTheRouteBetweenThemToRunLonger()
    {
        var repo = new MapMatchFakeRepository();
        // Four fixes three kilometres apart, and a road of sixteen kilometres through them: in a
        // city with one-way streets that is an ordinary route, not a detour.
        var handler = new MapMatchFakeValhallaHandler(TraceResponse(Road(0, 16), EvenlyAlong(4)));
        var svc = BuildService(repo, handler);

        var result = await svc.MatchAsync(Trace(4, spacingKm: 3), TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        Assert.Equal(MapMatchDefaults.Ttl, repo.LastTtl);
    }

    [Fact]
    public async Task MatchAsync_FixesMetresApart_DoNotAllowTheSameStretching()
    {
        var repo = new MapMatchFakeRepository();
        // The same proportions between a hundred metres apart: with fixes that dense the road has
        // nowhere to wander, so a route half as long again is the matcher losing the trace.
        var handler = new MapMatchFakeValhallaHandler(TraceResponse(Road(0, 0.53), EvenlyAlong(4)));
        var svc = BuildService(repo, handler);

        var result = await svc.MatchAsync(Trace(4, spacingKm: 0.1), TestContext.Current.CancellationToken);

        Assert.False(result.Matched);
    }

    [Fact]
    public async Task MatchAsync_FixTheMatcherCouldNotPlace_KeepsThePreviousVertex()
    {
        var repo = new MapMatchFakeRepository();
        var road = Road(0, 1.5);
        // The third of four fixes is unplaceable (a tunnel, a parallel service road).
        var handler = new MapMatchFakeValhallaHandler(TraceResponse(road, 0, 0.5, null, 1));
        var svc = BuildService(repo, handler);

        var result = await svc.MatchAsync(Trace(4), TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        Assert.Equal(result.PointIndexes![1], result.PointIndexes[2]);
        Assert.Equal(road.Count - 1, result.PointIndexes[3]);
    }

    [Fact]
    public async Task MatchAsync_SendsTheFixesAsSnappingHintsRatherThanWaypoints()
    {
        var repo = new MapMatchFakeRepository();
        var handler = new MapMatchFakeValhallaHandler(TraceResponse(Road(0, 1.5), EvenlyAlong(4)));
        var svc = BuildService(repo, handler);

        await svc.MatchAsync(Trace(4), TestContext.Current.CancellationToken);

        var body = handler.RequestBodies[0];
        Assert.Contains("\"shape_match\":\"map_snap\"", body);
        Assert.Contains("\"costing\":\"auto\"", body);
        Assert.Contains($"\"breakage_distance\":{MapMatchDefaults.BreakageDistanceMeters}", body);
    }

    [Fact]
    public async Task MatchAsync_AsksForSpeedLimitsInKilometres()
    {
        var repo = new MapMatchFakeRepository();
        var handler = new MapMatchFakeValhallaHandler(TraceResponse(Road(0, 1.5), EvenlyAlong(4)));
        var svc = BuildService(repo, handler);

        await svc.MatchAsync(Trace(4), TestContext.Current.CancellationToken);

        var body = handler.RequestBodies[0];
        Assert.Contains("edge.speed_limit", body);
        // Limits come back in whatever units the request asks for, so it says which.
        Assert.Contains("\"units\":\"kilometers\"", body);
    }

    [Fact]
    public async Task MatchAsync_SpeedLimits_CoverTheSegmentsOfTheRoadsUnderThem()
    {
        var repo = new MapMatchFakeRepository();
        var road = Road(0, 0.5, vertices: 31);
        // Three equal stretches: a 50 road, one OSM holds no limit for, and an 80 road.
        var handler = new MapMatchFakeValhallaHandler(TraceResponseWithLimits(road, [50, null, 80]));
        var svc = BuildService(repo, handler);

        var result = await svc.MatchAsync(Trace(2), TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        var limits = SpeedLimitRuns.Decode(result.SpeedLimits);

        // One limit per segment of the line, which is one fewer than it has vertices.
        Assert.Equal(road.Count - 1, limits.Count);
        Assert.Equal(50, limits[0]);
        Assert.Equal(50, limits[9]);
        Assert.Null(limits[10]);
        Assert.Null(limits[19]);
        Assert.Equal(80, limits[20]);
        Assert.Equal(80, limits[^1]);
    }

    [Fact]
    public async Task MatchAsync_UntaggedRoads_LeaveTheirSegmentsWithoutALimit()
    {
        var repo = new MapMatchFakeRepository();
        var road = Road(0, 0.5, vertices: 21);
        var handler = new MapMatchFakeValhallaHandler(TraceResponseWithLimits(road, [null]));
        var svc = BuildService(repo, handler);

        var result = await svc.MatchAsync(Trace(2), TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        // Nothing worth remembering, so nothing is written: the line still draws, uncoloured.
        Assert.Empty(result.SpeedLimits!);
        Assert.Empty(repo.LastUpsert!.SpeedLimitRuns);
    }

    [Fact]
    public async Task MatchAsync_TraceWithAHole_LeavesTheJumpBetweenPartsWithoutALimit()
    {
        var repo = new MapMatchFakeRepository();
        var before = Road(0, 1.0, vertices: 21);
        var after = Road(30, 31.0, vertices: 21);
        var handler = new MapMatchFakeValhallaHandler(
            TraceResponseWithLimits(before, [100], fixCount: 3),
            TraceResponseWithLimits(after, [80], fixCount: 3));
        var svc = BuildService(repo, handler);

        List<GeoPoint> trace = [Fix(0), Fix(0.5), Fix(1.0), Fix(30), Fix(30.5), Fix(31.0)];
        var result = await svc.MatchAsync(trace, TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        var limits = SpeedLimitRuns.Decode(result.SpeedLimits);

        // Both stretches keep their own limit, and the straight jump between them - which is a
        // segment of the stitched line like any other - carries none.
        Assert.Equal(before.Count + after.Count - 1, limits.Count);
        Assert.Equal(100, limits[before.Count - 2]);
        Assert.Null(limits[before.Count - 1]);
        Assert.Equal(80, limits[before.Count]);
    }

    [Fact]
    public async Task MatchAsync_UnsnappedStretch_CarriesNoLimitsOfItsOwn()
    {
        var repo = new MapMatchFakeRepository();
        var after = Road(30, 31.0, vertices: 21);
        var handler = new MapMatchFakeValhallaHandler(
            // The first part snaps to a five-kilometre detour for a one-kilometre trace, so it is
            // left as the raw fixes it always was; the second part matches normally.
            TraceResponse(Road(0, 5), EvenlyAlong(3)),
            TraceResponseWithLimits(after, [80], fixCount: 3));
        var svc = BuildService(repo, handler);

        List<GeoPoint> trace = [Fix(0), Fix(0.5), Fix(1.0), Fix(30), Fix(30.5), Fix(31.0)];
        var result = await svc.MatchAsync(trace, TestContext.Current.CancellationToken);

        Assert.True(result.Matched);
        var limits = SpeedLimitRuns.Decode(result.SpeedLimits);

        // Three raw fixes, then the jump, then the snapped stretch: only the last of those knows
        // what was signposted.
        Assert.Equal(3 + after.Count - 1, limits.Count);
        Assert.All(limits.Take(3), limit => Assert.Null(limit));
        Assert.Equal(80, limits[3]);
    }
}
