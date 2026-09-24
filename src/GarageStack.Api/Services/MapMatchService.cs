using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Services;

namespace GarageStack.Api.Services;

/// <summary>
/// Snaps a trip's GPS fixes onto the roads OSM says they were driven on: cache first, and only a
/// trace nothing has asked about yet goes to the matcher. Telemetry arrives as fixes tens of
/// seconds apart, so a raw trip line cuts every corner and drives through buildings; the snapped
/// line follows the roads between those fixes, and its length is the better distance for the trip.
/// <para>
/// A trace with a hole in it (no telemetry for a while) is split at the hole and each part
/// matched on its own, because a matcher refuses a trace that jumps kilometres in one step. The
/// parts are stitched back into one line, with the hole left as the straight line it always was.
/// </para>
/// </summary>
public sealed class MapMatchService(
    IMapMatchRepository repository,
    ValhallaApiClient client,
    ILogger<MapMatchService> logger)
{
    public async Task<MapMatchResult> MatchAsync(IReadOnlyList<GeoPoint> points, CancellationToken ct = default)
    {
        if (!client.IsEnabled) return MapMatchResult.Unavailable;

        var trace = points.Select(p => new GeoCoordinate(p.Lat, p.Lng)).ToList();
        var hash = HashOf(trace);

        if (await repository.GetValidAsync(ValhallaApiClient.Provider, hash, ct) is { } cached)
            return FromCache(cached);

        var segments = TraceSegmenter.Split(trace, MapMatchDefaults.MaxGapKm);
        var shape = new List<GeoCoordinate>(trace.Count);
        var indexes = new int[trace.Count];
        var anyMatched = false;

        foreach (var segment in segments)
        {
            var slice = trace.GetRange(segment.Start, segment.Count);
            var match = slice.Count >= MapMatchDefaults.MinPointsPerRequest
                ? await client.MatchAsync(slice, ct)
                : TraceMatch.NotMatched;

            // Could not ask upstream at all: answer "not yet" rather than caching a trip as
            // unmatchable when nothing has actually looked at it.
            if (match.Status == TraceMatchStatus.Unavailable) return MapMatchResult.Retry;

            var offset = shape.Count;
            if (match.Status == TraceMatchStatus.Matched && IsPlausible(slice, match.Shape))
            {
                AppendMatched(shape, indexes, segment, match, offset);
                anyMatched = true;
            }
            else
            {
                AppendRaw(shape, indexes, segment, slice, offset);
            }
        }

        var result = anyMatched
            ? new CachedTraceMatch(PolylineCodec.Encode(shape), indexes, Math.Round(TraceSegmenter.LengthKm(shape), 2))
            : CachedTraceMatch.NotMatched;

        await repository.UpsertAsync(ValhallaApiClient.Provider, hash, result,
            anyMatched ? MapMatchDefaults.Ttl : MapMatchDefaults.NegativeTtl, ct);

        if (!anyMatched)
            logger.LogDebug("No part of a {Count}-point trace in {Segments} segment(s) snapped to a road",
                trace.Count, segments.Count);

        return FromCache(result);
    }

    private static void AppendMatched(
        List<GeoCoordinate> shape, int[] indexes, TraceSegment segment, TraceMatch match, int offset)
    {
        shape.AddRange(match.Shape);

        var last = offset;
        for (var i = 0; i < segment.Count; i++)
        {
            // A fix the matcher could not place keeps the previous one's vertex, and the mapping
            // only ever moves forward: both keep per-fix data (a speed reading) in step with the
            // line when it is drawn, which is the only thing these indexes are for.
            if (match.ShapeIndexOfPoint[i] is { } mapped)
                last = Math.Max(last, offset + Math.Clamp(mapped, 0, match.Shape.Count - 1));
            indexes[segment.Start + i] = last;
        }
    }

    private static void AppendRaw(
        List<GeoCoordinate> shape, int[] indexes, TraceSegment segment, List<GeoCoordinate> slice, int offset)
    {
        shape.AddRange(slice);
        for (var i = 0; i < segment.Count; i++)
            indexes[segment.Start + i] = offset + i;
    }

    /// <summary>
    /// Whether a snapped stretch is close enough in length to the fixes it came from to be the
    /// road they were driven on. Sparse fixes leave a matcher room to pick a plausible route that
    /// is not the one taken, and a detour shows up as a length that no longer resembles the trace
    /// - but so, legitimately, does a city route between fixes a kilometre apart, which is why the
    /// allowance grows with the spacing (see <see cref="MapMatchDefaults.LengthRatioBase"/>).
    /// </summary>
    private static bool IsPlausible(List<GeoCoordinate> slice, IReadOnlyList<GeoCoordinate> matchedShape)
    {
        var rawKm = TraceSegmenter.LengthKm(slice);
        if (rawKm <= 0) return false;

        var meanGapKm = rawKm / (slice.Count - 1);
        var ceiling = Math.Min(
            MapMatchDefaults.LengthRatioBase + MapMatchDefaults.LengthRatioPerGapKm * meanGapKm,
            MapMatchDefaults.MaxLengthRatioCeiling);

        var ratio = TraceSegmenter.LengthKm(matchedShape) / rawKm;
        return ratio >= MapMatchDefaults.MinLengthRatio && ratio <= ceiling;
    }

    private static MapMatchResult FromCache(CachedTraceMatch match) =>
        match.Shape is null
            ? MapMatchResult.NotMatched
            : new MapMatchResult(true, true, false, match.Shape, match.PointIndexes, match.MatchedKm);

    /// <summary>
    /// Addresses a trace in the cache. Coordinates are rounded to about a metre first, so the same
    /// trip asked about twice is one row however the fixes were formatted on the way in.
    /// </summary>
    private static string HashOf(IReadOnlyList<GeoCoordinate> trace)
    {
        var builder = new StringBuilder(trace.Count * 24);
        builder.Append(MapMatchDefaults.CacheVersion).Append('|');
        foreach (var point in trace)
        {
            builder.Append(point.Lat.ToString("F5", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(point.Lng.ToString("F5", CultureInfo.InvariantCulture)).Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}

/// <summary>
/// A trip's snapped line, or the reason there is none.
/// </summary>
/// <param name="Available">False when matching is switched off for this deployment, so a client can stop asking.</param>
/// <param name="Matched">True when <paramref name="Shape"/> holds a snapped line to draw instead of the raw fixes.</param>
/// <param name="Pending">True when the matcher could not be reached just now and a later request may succeed.</param>
/// <param name="Shape">The snapped line as an encoded polyline (six decimals).</param>
/// <param name="PointIndexes">One vertex index into <paramref name="Shape"/> per fix that was sent, in order.</param>
/// <param name="MatchedKm">Length of the snapped line, which beats the straight-line distance through the fixes.</param>
public sealed record MapMatchResult(
    bool Available,
    bool Matched,
    bool Pending,
    string? Shape,
    IReadOnlyList<int>? PointIndexes,
    double MatchedKm)
{
    public static readonly MapMatchResult Unavailable = new(false, false, false, null, null, 0);
    public static readonly MapMatchResult Retry = new(true, false, true, null, null, 0);
    public static readonly MapMatchResult NotMatched = new(true, false, false, null, null, 0);
}
