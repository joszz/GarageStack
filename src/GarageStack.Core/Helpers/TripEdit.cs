using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// The rules for editing a saved trip's route, shared by the real and the demo trip repositories.
/// Only positions are edited: a point keeps the time the car reported it at, and a point someone
/// adds gets a time between its neighbours from the editor.
/// </summary>
public static class TripEdit
{
    /// <summary>How many points a save may add to the trip as recorded.</summary>
    public const int MaxAddedPoints = 200;

    /// <summary>An end moved further than this has a different address, so it is looked up again.</summary>
    public const double AddressMovedMetres = 50;

    // A sent point this close in time to a recorded one, at the same position, is that point. The
    // database keeps microseconds, a browser's clock milliseconds.
    private static readonly TimeSpan SameTime = TimeSpan.FromMilliseconds(1);
    private const double SameDegrees = 1e-9;

    /// <summary>The most points a save may send, for a trip recorded with <paramref name="originalCount"/>.</summary>
    public static int MaxPoints(int originalCount) => originalCount + MaxAddedPoints;

    /// <summary>
    /// Checks the points an editor sent, and turns them into the trip's new points: a point that is
    /// one of <paramref name="original"/> stays that point, speed and source included, and any
    /// other is <see cref="TripPointSource.Manual"/>, without a speed, whatever the client said.
    /// </summary>
    /// <returns>Why the points are refused, or null with <paramref name="points"/> set.</returns>
    public static TripEditProblem? TryBuild(
        IReadOnlyList<TripPointInput>? input,
        IReadOnlyList<TripPoint> original,
        DateTime? notBefore,
        DateTime? notAfter,
        out IReadOnlyList<TripPoint> points)
    {
        points = [];
        var max = MaxPoints(original.Count);
        if (input is null || input.Count < 2 || input.Count > max)
            return new("trip.pointCount", $"A trip needs 2 to {max} points");

        var built = new List<TripPoint>(input.Count);
        foreach (var p in input)
        {
            if (p is not { RecordedAt: { } at, Latitude: { } lat, Longitude: { } lon } ||
                !double.IsFinite(lat) || !double.IsFinite(lon) || Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
                return new("trip.pointInvalid", "Every point needs a time, a latitude from -90 to 90 and a longitude from -180 to 180");

            built.Add(Recorded(original, at.UtcDateTime, lat, lon) ??
                      new TripPoint(at.UtcDateTime, lat, lon, null, TripPointSource.Manual));
        }

        for (var i = 1; i < built.Count; i++)
        {
            if (built[i].RecordedAt < built[i - 1].RecordedAt)
                return new("trip.pointOrder", "Points must be in the order they were driven");
        }

        if (notBefore is { } earliest && built[0].RecordedAt < earliest)
            return new("trip.overlapsPrevious", "The trip may not start before the trip before it ended");
        if (notAfter is { } latest && built[^1].RecordedAt > latest)
            return new("trip.overlapsNext", "The trip may not end after the next trip started");

        for (var i = 1; i < built.Count; i++)
        {
            if (!IsPlausibleStep(built[i - 1], built[i]))
                return new("trip.pointTooFast", $"No step may be faster than {TripSegmenter.MaxPlausibleSpeedKmh:0} km/h");
        }

        points = built;
        return null;
    }

    /// <summary>The trip <paramref name="points"/> make, with its distance and figures read off them.</summary>
    public static TripDto Summarize(IReadOnlyList<TripPoint> points, long? id = null)
    {
        var distance = 0.0;
        for (var i = 1; i < points.Count; i++)
            distance += GeoHelper.Haversine(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
        return new TripDto(0, points[0].RecordedAt, points[^1].RecordedAt, Math.Round(distance, 2), points.Count, points, id);
    }

    /// <summary>Whether an end at <paramref name="latitude"/>, <paramref name="longitude"/> moved far enough to <paramref name="now"/> to need its address again.</summary>
    public static bool AddressMoved(double latitude, double longitude, TripPoint now) =>
        GeoHelper.Haversine(latitude, longitude, now.Latitude, now.Longitude) * 1000 > AddressMovedMetres;

    /// <summary>Where the car stood before a trip, left at <paramref name="departure"/>.</summary>
    public static TripStop Departing(TripPoint stop, DateTime departure) =>
        new(stop.Latitude, stop.Longitude, stop.RecordedAt, departure);

    /// <summary>
    /// Where the car stood after the trip ending at <paramref name="last"/>: reached at the parked
    /// fix's own time, or for the next trip's start, which says only when the car left it, the gap
    /// at <see cref="TripAnchor.AssumedSpeedKmh"/> after <paramref name="last"/>, but no later than
    /// that start.
    /// </summary>
    public static TripStop Arriving(TripPoint last, TripPoint stop, bool isParkedFix)
    {
        if (isParkedFix) return new(stop.Latitude, stop.Longitude, stop.RecordedAt, stop.RecordedAt);

        var gapKm = GeoHelper.Haversine(last.Latitude, last.Longitude, stop.Latitude, stop.Longitude);
        var estimate = last.RecordedAt + TimeSpan.FromHours(gapKm / TripAnchor.AssumedSpeedKmh);
        return new(stop.Latitude, stop.Longitude, stop.RecordedAt, estimate < stop.RecordedAt ? estimate : stop.RecordedAt);
    }

    /// <summary>The editor for a trip with these points and neighbours.</summary>
    public static TripEditor Editor(
        long id,
        IReadOnlyList<TripPoint> points,
        IReadOnlyList<TripPoint>? original,
        DateTime? editedAt,
        TripStop? previousStop,
        TripStop? nextStop,
        DateTime? notBefore,
        DateTime? notAfter)
    {
        var suggestion = previousStop is { } stop &&
                         TripAnchor.IsCandidate(new TripPoint(stop.RecordedAt, stop.Latitude, stop.Longitude, null), points[0]);
        return new(id, points, original, editedAt, previousStop, nextStop, suggestion, notBefore, notAfter,
            MaxPoints((original ?? points).Count));
    }

    // The recorded point this one is, when it is one.
    private static TripPoint? Recorded(IReadOnlyList<TripPoint> original, DateTime at, double lat, double lon)
    {
        foreach (var p in original)
        {
            if ((p.RecordedAt - at).Duration() < SameTime &&
                Math.Abs(p.Latitude - lat) < SameDegrees && Math.Abs(p.Longitude - lon) < SameDegrees)
                return p;
        }

        return null;
    }

    // Between two recorded points the segmenter's own rule holds, which lets two fixes share a
    // time. A step to or from a placed point must take time as well.
    private static bool IsPlausibleStep(TripPoint from, TripPoint to)
    {
        if (from.Source != TripPointSource.Manual && to.Source != TripPointSource.Manual &&
            from.RecordedAt == to.RecordedAt)
            return true;

        return GeoHelper.Haversine(from.Latitude, from.Longitude, to.Latitude, to.Longitude) == 0 ||
               TripSegmenter.IsPlausibleHop(from, to);
    }
}
