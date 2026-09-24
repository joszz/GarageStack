using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>A run of consecutive trace points, both ends included.</summary>
public readonly record struct TraceSegment(int Start, int End)
{
    public int Count => End - Start + 1;
}

/// <summary>
/// Cuts a trip's fixes into the runs a matcher can work with, and measures a line. A hole in the
/// telemetry (the gateway stopped publishing for a while, the car went through a tunnel) leaves
/// two fixes kilometres apart; a matcher refuses a whole trace containing a jump that wide, so
/// each side of the hole is matched separately and the jump itself stays the straight line it
/// always was.
/// </summary>
public static class TraceSegmenter
{
    public static IReadOnlyList<TraceSegment> Split(IReadOnlyList<GeoCoordinate> points, double maxGapKm)
    {
        if (points.Count == 0) return [];

        var segments = new List<TraceSegment>();
        var start = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var gapKm = GeoHelper.Haversine(points[i - 1].Lat, points[i - 1].Lng, points[i].Lat, points[i].Lng);
            if (gapKm <= maxGapKm) continue;
            segments.Add(new TraceSegment(start, i - 1));
            start = i;
        }

        segments.Add(new TraceSegment(start, points.Count - 1));
        return segments;
    }

    /// <summary>Length of a line along its vertices, in kilometres.</summary>
    public static double LengthKm(IReadOnlyList<GeoCoordinate> shape)
    {
        var total = 0.0;
        for (var i = 1; i < shape.Count; i++)
            total += GeoHelper.Haversine(shape[i - 1].Lat, shape[i - 1].Lng, shape[i].Lat, shape[i].Lng);
        return total;
    }
}
