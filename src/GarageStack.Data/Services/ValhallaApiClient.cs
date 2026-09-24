using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Services;

/// <summary>
/// Map matching against Valhalla (the router behind OpenStreetMap's own directions, or a
/// self-hosted instance via <c>MapMatching:BaseUrl</c>). Its <c>trace_attributes</c> action takes
/// a trip's GPS fixes and answers with the roads underneath them, which is what turns a line that
/// cuts corners between sparse fixes into the route that was actually driven.
/// <para>
/// The public instance is shared infrastructure, so requests go out one at a time and every
/// answer is cached by <see cref="GarageStack.Core.Interfaces.IMapMatchRepository"/>; this client
/// is only reached for a trace nothing has asked about yet.
/// </para>
/// </summary>
public sealed class ValhallaApiClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ValhallaApiClient> logger)
{
    public const string HttpClientName = "valhalla";

    /// <summary>Names the matcher whose answers the cache holds, so a switch invalidates them.</summary>
    public const string Provider = "valhalla";

    // Singleton-level gate: one request in flight at a time, at most one per MinInterval.
    private readonly UpstreamRateGate _gate = new();

    // Matching a trace is heavier upstream than a geocode lookup, so this is deliberately no
    // faster than one request per second even though nothing formally demands it.
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    // A caller is a browser waiting for a trip it just selected. A long trip is split into a few
    // traces, so the budget allows queueing behind a couple of them before giving up for now.
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RetryAfter429 = TimeSpan.FromSeconds(60);

    private const string DefaultBaseUrl = "https://valhalla1.openstreetmap.de";

    /// <summary>
    /// Deployments that would rather not send trip geometry to a third party can set
    /// <c>MapMatching:Enabled=false</c> (and keep snapped trips by pointing BaseUrl at their own
    /// Valhalla). Anything other than an explicit "false" leaves matching on.
    /// </summary>
    public bool IsEnabled =>
        !string.Equals(configuration["MapMatching:Enabled"], "false", StringComparison.OrdinalIgnoreCase);

    private string BaseUrl =>
        (configuration["MapMatching:BaseUrl"] ?? DefaultBaseUrl).TrimEnd('/');

    /// <summary>
    /// Snaps one trace onto roads. Returns <see cref="TraceMatch.NotMatched"/> when the matcher
    /// answered but recognised no road under these fixes (a cacheable answer), and
    /// <see cref="TraceMatch.Unavailable"/> when we could not ask at all - the gate was held, a
    /// backoff window was open, or the request failed - so the caller leaves the trip unsnapped
    /// rather than caching a miss it never actually made.
    /// </summary>
    public async Task<TraceMatch> MatchAsync(IReadOnlyList<GeoCoordinate> points, CancellationToken ct = default)
    {
        if (!IsEnabled || points.Count < MapMatchDefaults.MinPointsPerRequest) return TraceMatch.Unavailable;

        // Pre-gate check: skip the queue entirely while a backoff window is open.
        if (_gate.IsBackingOff)
        {
            logger.LogDebug("Valhalla backoff active, leaving a {Count}-point trace unsnapped", points.Count);
            return TraceMatch.Unavailable;
        }

        if (!await _gate.WaitAsync(GateTimeout, ct))
        {
            logger.LogDebug("Valhalla gate busy, leaving a {Count}-point trace unsnapped", points.Count);
            return TraceMatch.Unavailable;
        }

        try
        {
            // Re-check inside the gate: the request we queued behind may have been rate-limited.
            if (_gate.IsBackingOff)
            {
                logger.LogDebug("Valhalla backoff active (inside gate), leaving a {Count}-point trace unsnapped", points.Count);
                return TraceMatch.Unavailable;
            }

            await _gate.ThrottleAsync(MinInterval, honourBackoff: true, ct);

            var client = httpClientFactory.CreateClient(HttpClientName);
            _gate.MarkRequestSent();
            using var response = await client.PostAsJsonAsync($"{BaseUrl}/trace_attributes", BuildRequest(points), ct);

            if (UpstreamRateGate.IsThrottlingStatus((int)response.StatusCode))
            {
                _gate.SetBackoff(RetryAfter429);
                logger.LogDebug("Valhalla {Status}, backing off {Seconds}s",
                    (int)response.StatusCode, (int)RetryAfter429.TotalSeconds);
                return TraceMatch.Unavailable;
            }

            // A 400 is the matcher's way of saying these fixes snap to nothing it recognises
            // (no road within reach, or a jump it will not bridge). That is an answer about the
            // trace rather than a fault, so it is worth remembering for a while.
            if ((int)response.StatusCode == 400)
            {
                logger.LogDebug("Valhalla could not snap a {Count}-point trace", points.Count);
                return TraceMatch.NotMatched;
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync<TraceAttributesResponse>(stream, cancellationToken: ct);
            return result is null ? TraceMatch.NotMatched : MapResponse(result, points.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Coordinates stay out of the log: a failing matcher is worth knowing about, where the
            // car has been is not worth writing to disk on every retry.
            logger.LogWarning(ex, "Valhalla map matching failed for a {Count}-point trace", points.Count);
            return TraceMatch.Unavailable;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static TraceAttributesRequest BuildRequest(IReadOnlyList<GeoCoordinate> points) => new()
    {
        Shape = [.. points.Select(p => new TraceShapePoint { Lat = p.Lat, Lon = p.Lng })],
        BreakageDistance = MapMatchDefaults.BreakageDistanceMeters,
        SearchRadius = MapMatchDefaults.SearchRadiusMeters,
        GpsAccuracy = MapMatchDefaults.GpsAccuracyMeters,
    };

    /// <summary>
    /// Turns the matcher's answer into the shape plus, per fix we sent, its vertex along that
    /// shape, and the speed limit on each of the shape's segments. The answer says which edge a fix
    /// landed on and how far along it; the edge in turn says which slice of the shape it covers, so
    /// the two together place the fix on the line and spread the edge's limit over that slice.
    /// </summary>
    private static TraceMatch MapResponse(TraceAttributesResponse response, int pointCount)
    {
        var shape = PolylineCodec.Decode(response.Shape);
        if (shape.Count < 2) return TraceMatch.NotMatched;

        var edges = response.Edges ?? [];
        var matched = response.MatchedPoints ?? [];
        var indexes = new int?[pointCount];
        var limits = new int?[shape.Count - 1];

        foreach (var edge in edges)
        {
            // Absent rather than zero is what a road without a maxspeed tag answers with; the
            // edge's own "speed" is a routing estimate and says nothing about what is signposted.
            if (edge.SpeedLimit is not > 0) continue;

            var from = Math.Clamp(edge.BeginShapeIndex, 0, limits.Length);
            var to = Math.Clamp(edge.EndShapeIndex, from, limits.Length);
            for (var i = from; i < to; i++) limits[i] = edge.SpeedLimit;
        }

        for (var i = 0; i < pointCount && i < matched.Length; i++)
        {
            var point = matched[i];
            // Unmatched fixes carry no usable edge: older builds answer with the largest possible
            // index rather than leaving the field out, which is why it is read as unsigned.
            if (point.EdgeIndex is not { } edgeIndex || edgeIndex >= (ulong)edges.Length) continue;

            var edge = edges[(int)edgeIndex];
            var begin = Math.Clamp(edge.BeginShapeIndex, 0, shape.Count - 1);
            var end = Math.Clamp(edge.EndShapeIndex, begin, shape.Count - 1);
            var along = Math.Clamp(point.DistanceAlongEdge ?? 0, 0, 1);
            indexes[i] = begin + (int)Math.Round(along * (end - begin));
        }

        return indexes.Any(i => i is not null)
            ? new TraceMatch(TraceMatchStatus.Matched, shape, indexes, limits)
            : TraceMatch.NotMatched;
    }

    private sealed class TraceAttributesRequest
    {
        [JsonPropertyName("shape")] public TraceShapePoint[] Shape { get; init; } = [];
        [JsonPropertyName("costing")] public string Costing => "auto";

        /// <summary>Snap the fixes to roads rather than treating them as waypoints to route between.</summary>
        [JsonPropertyName("shape_match")] public string ShapeMatch => "map_snap";

        [JsonPropertyName("breakage_distance")] public int BreakageDistance { get; init; }
        [JsonPropertyName("search_radius")] public int SearchRadius { get; init; }
        [JsonPropertyName("gps_accuracy")] public int GpsAccuracy { get; init; }

        /// <summary>Kilometres is the default, but speed limits come back in whatever this says.</summary>
        [JsonPropertyName("units")] public string Units => "kilometers";

        /// <summary>Only the attributes below are wanted; the full answer is many times the size.</summary>
        [JsonPropertyName("filters")] public TraceFilters Filters => new();
    }

    private sealed class TraceFilters
    {
        [JsonPropertyName("attributes")]
        public string[] Attributes =>
        [
            "shape",
            "edge.begin_shape_index",
            "edge.end_shape_index",
            "edge.speed_limit",
            "matched.edge_index",
            "matched.distance_along_edge",
        ];

        [JsonPropertyName("action")] public string Action => "include";
    }

    private sealed class TraceShapePoint
    {
        [JsonPropertyName("lat")] public double Lat { get; init; }
        [JsonPropertyName("lon")] public double Lon { get; init; }
    }

    private sealed class TraceAttributesResponse
    {
        [JsonPropertyName("shape")] public string? Shape { get; init; }
        [JsonPropertyName("edges")] public TraceEdge[]? Edges { get; init; }
        [JsonPropertyName("matched_points")] public TraceMatchedPoint[]? MatchedPoints { get; init; }
    }

    private sealed class TraceEdge
    {
        [JsonPropertyName("begin_shape_index")] public int BeginShapeIndex { get; init; }
        [JsonPropertyName("end_shape_index")] public int EndShapeIndex { get; init; }

        /// <summary>The signposted limit in km/h, left out entirely for a road OSM has none for.</summary>
        [JsonPropertyName("speed_limit")] public int? SpeedLimit { get; init; }
    }

    private sealed class TraceMatchedPoint
    {
        [JsonPropertyName("edge_index")] public ulong? EdgeIndex { get; init; }
        [JsonPropertyName("distance_along_edge")] public double? DistanceAlongEdge { get; init; }
    }
}
