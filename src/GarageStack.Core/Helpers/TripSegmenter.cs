using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// The trips found in a run of GPS fixes. <see cref="OpenSince"/> is the first fix of the segment
/// that was still going at the horizon, or null when every segment had ended by then: a later fix
/// could still extend that segment, so it is shown but not final.
/// <para>
/// <see cref="ParkedBefore"/> lines up with <see cref="Trips"/> by <see cref="TripDto.Index"/>: the
/// last fix the car reported standing still before that trip moved off, or null when the fixes hold
/// none since the car last moved. Kept apart from the trips, so a caller that only shows them need
/// not know about it; <see cref="TripAnchor"/> decides whether the trip should start there.
/// </para>
/// </summary>
public sealed record TripSegmentation(IReadOnlyList<TripDto> Trips, DateTime? OpenSince, IReadOnlyList<TripPoint?> ParkedBefore)
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

    /// <summary>Consecutive fixes implying more than this speed are a positioning glitch, not a real trip.</summary>
    public const double MaxPlausibleSpeedKmh = 250;

    // A fix without a speed this close to the previous one is GPS drift, not movement: well above
    // GPS noise but well below any real movement between consecutive updates. Also how far the car
    // must have stood from its last moving fix for the trip to end at the parked fix instead.
    private const double DriftMetres = 50;

    // A segment shorter than this never went anywhere: drift, or a brief polling burst while parked.
    private const double MinTripKm = 0.1;

    /// <param name="fixes">GPS fixes, oldest first.</param>
    /// <param name="horizon">
    /// The moment the fixes are complete up to. A segment still going then is reported as open.
    /// </param>
    public static TripSegmentation Segment(IReadOnlyList<TripPoint> fixes, DateTime horizon)
    {
        var segments = new Segments();
        DateTime? lastSeen = null;
        // The first fix of the stop the car is in, or null while it moves.
        TripPoint? stoppedAt = null;
        // The latest fix the car reported standing still since it last moved. Unlike the rest, it
        // survives a hard gap: a car parked overnight still left from where it parked.
        TripPoint? lastParked = null;
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

            // Hard gap: no data at all, so a new trip starts whatever the car does next. A car that
            // had stopped before it went quiet ended its trip where it stopped.
            if (lastSeen.HasValue && p.RecordedAt - lastSeen.Value > GapThreshold)
            {
                segments.Close(stoppedAt);
                stoppedAt = null;
                prevLat = null;
                prevLon = null;
            }

            lastSeen = p.RecordedAt;

            if (isParked)
            {
                // Remember where the stop began; parked fixes are not part of the path.
                stoppedAt ??= p;
                lastParked = p;
                prevLat = p.Latitude;
                prevLon = p.Longitude;
                continue;
            }

            // Moving again: a new trip if the car stood still long enough to count as parked.
            if (stoppedAt is { } stop && p.RecordedAt - stop.RecordedAt >= ParkThreshold)
                segments.Close(stop);
            stoppedAt = null;

            segments.Add(p, lastParked);
            lastParked = null;

            prevLat = p.Latitude;
            prevLon = p.Longitude;
        }

        // The last segment is over only when no later fix could still extend it: fixes that arrive
        // past the horizon are either a hard gap after it, or come after the car had stood still
        // long enough to count as parked.
        DateTime? openSince = null;
        if (segments.OpenStart is { } start)
        {
            var ended = horizon - lastSeen!.Value > GapThreshold ||
                        (stoppedAt is { } since && horizon - since.RecordedAt >= ParkThreshold);
            if (!ended) openSince = start;
        }

        // An open segment has not stopped for good yet, so it ends at its last moving fix.
        segments.Close(openSince is null ? stoppedAt : null);
        return new TripSegmentation(segments.Trips, openSince, segments.ParkedBefore);
    }

    /// <summary>
    /// Whether a car could have gone from <paramref name="from"/> to <paramref name="to"/>: the
    /// later fix is later, and the speed the hop implies is not a positioning glitch.
    /// </summary>
    public static bool IsPlausibleHop(TripPoint from, TripPoint to)
    {
        var hours = (to.RecordedAt - from.RecordedAt).TotalHours;
        return hours > 0 &&
               GeoHelper.Haversine(from.Latitude, from.Longitude, to.Latitude, to.Longitude) / hours <= MaxPlausibleSpeedKmh;
    }

    // The trips cut so far and the segment being built.
    private sealed class Segments
    {
        private readonly List<TripPoint> _current = [];
        private TripPoint? _currentParkedBefore;

        public List<TripDto> Trips { get; } = [];
        public List<TripPoint?> ParkedBefore { get; } = [];

        public DateTime? OpenStart => _current.Count > 0 ? _current[0].RecordedAt : null;

        public void Add(TripPoint fix, TripPoint? parkedBefore)
        {
            if (_current.Count == 0) _currentParkedBefore = parkedBefore;

            // Skip consecutive duplicate positions (GPS cached, not updating while driving).
            if (_current.Count == 0 || !SamePosition(_current[^1], fix))
                _current.Add(fix);
        }

        /// <param name="parkedAt">
        /// The first fix of the stop the segment ended in, or null when it did not end standing
        /// still. The car reports where it parked a poll or so after its last moving fix, so the
        /// trip ends there instead, unless that hop is drift or a glitch. Only a trip that went
        /// somewhere gets it: one moving fix and a parked one is no trip.
        /// </param>
        public void Close(TripPoint? parkedAt)
        {
            if (parkedAt is not null && _current.Count >= 2 && IsWorthEnding(_current[^1], parkedAt))
                _current.Add(parkedAt with { Source = TripPointSource.Anchored });

            if (_current.Count >= 2 && BuildTrip(Trips.Count, _current) is { } trip)
            {
                Trips.Add(trip);
                ParkedBefore.Add(_currentParkedBefore);
            }

            _current.Clear();
            _currentParkedBefore = null;
        }

        private static bool IsWorthEnding(TripPoint last, TripPoint parkedAt) =>
            GeoHelper.Haversine(last.Latitude, last.Longitude, parkedAt.Latitude, parkedAt.Longitude) * 1000 > DriftMetres &&
            IsPlausibleHop(last, parkedAt);
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
