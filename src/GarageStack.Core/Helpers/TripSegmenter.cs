using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// The trips found in a run of GPS fixes. <see cref="OpenSince"/> is the first fix of the segment
/// that was still going at the horizon, or null when every segment had ended by then: a later fix
/// could still extend that segment, so it is shown but not final.
/// </summary>
public sealed record TripSegmentation(IReadOnlyList<TripDto> Trips, DateTime? OpenSince)
{
    /// <summary>The trips no later fix can change: every one that started before the open segment.</summary>
    public IReadOnlyList<TripDto> Closed =>
        OpenSince is { } open ? [.. Trips.Where(t => t.StartedAt < open)] : Trips;
}

/// <summary>
/// Cuts GPS fixes into trips. The one definition of a trip, shared by the Worker that saves
/// finished trips and the Api that shows the trips not saved yet, so both cut the same fixes into
/// the same trips.
/// </summary>
public static class TripSegmenter
{
    /// <summary>No telemetry at all for longer than this ends a trip, whatever the car was doing.</summary>
    public static readonly TimeSpan GapThreshold = TimeSpan.FromMinutes(30);

    /// <summary>Standing still for this long ends a trip; a shorter stop is traffic.</summary>
    public static readonly TimeSpan ParkThreshold = TimeSpan.FromMinutes(5);

    // Consecutive fixes implying more than this speed are a positioning glitch, not a real trip.
    private const double MaxPlausibleSpeedKmh = 250;

    // A fix without a speed this close to the previous one is GPS drift, not movement: well above
    // GPS noise but well below any real movement between consecutive updates.
    private const double DriftMetres = 50;

    // A segment shorter than this never went anywhere: drift, or a brief polling burst while parked.
    private const double MinTripKm = 0.1;

    /// <param name="fixes">GPS fixes, oldest first.</param>
    /// <param name="horizon">
    /// The moment the fixes are complete up to. A segment still going then is reported as open.
    /// </param>
    public static TripSegmentation Segment(IReadOnlyList<TripPoint> fixes, DateTime horizon)
    {
        var trips = new List<TripDto>();
        var current = new List<TripPoint>();
        DateTime? lastSeen = null;
        DateTime? parkingSince = null;
        // Last known position, used to tell a stationary fix without a speed from a moving one.
        double? prevLat = null, prevLon = null;

        foreach (var p in fixes)
        {
            // Fixes from the location/position topic never carry a speed: that arrives on its own
            // topic, often in a separate row. Such a fix counts as stationary only when it has not
            // moved significantly from the last one.
            var isParked = p.Speed is { } speed
                ? speed <= 0
                : prevLat.HasValue &&
                  GeoHelper.Haversine(prevLat.Value, prevLon!.Value, p.Latitude, p.Longitude) * 1000 <= DriftMetres;

            // Hard gap: no data at all, so a new trip starts whatever the car does next.
            if (lastSeen.HasValue && p.RecordedAt - lastSeen.Value > GapThreshold)
            {
                Close(trips, current);
                parkingSince = null;
                prevLat = null;
                prevLon = null;
            }

            lastSeen = p.RecordedAt;

            if (isParked)
            {
                // Remember when the stationary period began; parked fixes are not part of the path.
                parkingSince ??= p.RecordedAt;
                prevLat = p.Latitude;
                prevLon = p.Longitude;
                continue;
            }

            // Moving again: a new trip if the car stood still long enough to count as parked.
            if (parkingSince.HasValue && p.RecordedAt - parkingSince.Value >= ParkThreshold)
                Close(trips, current);
            parkingSince = null;

            // Skip consecutive duplicate positions (GPS cached, not updating while driving).
            if (current.Count == 0 || !SamePosition(current[^1], p))
                current.Add(p);

            prevLat = p.Latitude;
            prevLon = p.Longitude;
        }

        // The last segment is over only when no later fix could still extend it: fixes that arrive
        // past the horizon are either a hard gap after it, or come after the car had stood still
        // long enough to count as parked.
        DateTime? openSince = null;
        if (current.Count > 0)
        {
            var ended = horizon - lastSeen!.Value > GapThreshold ||
                        (parkingSince is { } since && horizon - since >= ParkThreshold);
            if (!ended) openSince = current[0].RecordedAt;
        }

        Close(trips, current);
        return new TripSegmentation(trips, openSince);
    }

    private static void Close(List<TripDto> trips, List<TripPoint> current)
    {
        if (current.Count >= 2 && BuildTrip(trips.Count, current) is { } trip)
            trips.Add(trip);
        current.Clear();
    }

    // Two positions are considered identical when within ~1 metre of each other.
    private static bool SamePosition(TripPoint a, TripPoint b) =>
        Math.Abs(a.Latitude - b.Latitude) < 0.00001 &&
        Math.Abs(a.Longitude - b.Longitude) < 0.00001;

    // Sums the segment distances and, in the same pass, rejects GPS teleportation: a trip with any
    // segment implying an impossible speed is not a real trip.
    private static TripDto? BuildTrip(int index, List<TripPoint> points)
    {
        var distance = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            var segKm = GeoHelper.Haversine(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
            var segHours = (points[i].RecordedAt - points[i - 1].RecordedAt).TotalHours;
            if (segHours > 0 && segKm / segHours > MaxPlausibleSpeedKmh)
                return null;
            distance += segKm;
        }

        var distanceKm = Math.Round(distance, 2);
        if (distanceKm < MinTripKm) return null;

        // A copy: the caller clears its list for the next segment.
        return new TripDto(index, points[0].RecordedAt, points[^1].RecordedAt, distanceKm, points.Count, [.. points]);
    }
}
