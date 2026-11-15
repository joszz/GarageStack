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
/// <para>
/// <see cref="Unseen"/> are the drives no poll caught: the car went quiet in one place and was next
/// seen standing in another, long enough to have parked there, with at most one moving fix in
/// between. Each one is final, as a drive whose end is not settled yet holds
/// <see cref="OpenSince"/> back instead. Candidates only, since a tow truck or a GPS jump looks the
/// same; <see cref="UnseenTrip"/> decides on the odometer.
/// </para>
/// </summary>
public sealed record TripSegmentation(
    IReadOnlyList<TripDto> Trips, DateTime? OpenSince, IReadOnlyList<TripPoint?> ParkedBefore, IReadOnlyList<UnseenDrive> Unseen)
{
    /// <summary>The trips no later fix can change: every one that started before the open segment.</summary>
    public IReadOnlyList<TripDto> Closed =>
        OpenSince is { } open ? [.. Trips.Where(t => t.StartedAt < open)] : Trips;
}

/// <summary>
/// A drive no poll caught, as the fixes around it show it: the spot the car stood at, the moving fix
/// a poll caught on the way if there was one, and the spot it was next seen standing at. The spots
/// are <see cref="TripPointSource.Anchored"/>.
/// </summary>
public sealed record UnseenDrive(IReadOnlyList<TripPoint> Points);

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

    /// <summary>
    /// Whether the car stood still in <paramref name="fix"/>, the fix after <paramref name="previous"/>.
    /// Fixes from the location/position topic never carry a speed: that arrives on its own topic,
    /// often in a separate row. Such a fix counts as standing still only when it has not moved
    /// significantly from the one before, however long ago that was.
    /// </summary>
    public static bool IsParked(TripPoint fix, TripPoint? previous) =>
        fix.Speed is { } speed
            ? speed <= 0
            : previous is not null && IsDrift(previous, fix);

    /// <param name="fixes">GPS fixes, oldest first.</param>
    /// <param name="horizon">
    /// The moment the fixes are complete up to. A segment still going then is reported as open.
    /// </param>
    /// <param name="parkedBefore">
    /// The fix before <paramref name="fixes"/>, when the car stood still in it by
    /// <see cref="IsParked"/>: where a trip or an unseen drive at the start of the fixes left from.
    /// </param>
    public static TripSegmentation Segment(IReadOnlyList<TripPoint> fixes, DateTime horizon, TripPoint? parkedBefore = null)
    {
        var segments = new Segments();
        DateTime? lastSeen = null;
        TripPoint? previous = null;
        // The first fix of the stop the car is in, or null while it moves.
        TripPoint? stoppedAt = null;
        // The latest fix the car reported standing still since it last moved. Unlike the rest, it
        // survives a hard gap: a car parked overnight still left from where it parked.
        TripPoint? lastParked = parkedBefore;
        // A drive no poll caught, to where the car stands now, until it has stood there long enough
        // to have parked: a car first seen waiting at a red light drives on.
        UnseenDrive? pending = null;
        // The one moving fix a poll caught after the car left lastParked, when it went quiet again
        // before it was seen standing: the drive goes on to wherever that is.
        TripPoint? caught = null;

        void ConfirmPending()
        {
            if (pending is { } drive) segments.Unseen.Add(drive);
            pending = null;
        }

        foreach (var p in fixes)
        {
            var isParked = IsParked(p, previous);
            previous = p;

            // Hard gap: no data at all, so a new trip starts whatever the car does next. A car that
            // had stopped before it went quiet ended its trip, or the drive no poll caught, where it
            // stopped.
            if (lastSeen.HasValue && p.RecordedAt - lastSeen.Value > GapThreshold)
            {
                if (segments.Close(stoppedAt) is { } onTheWay)
                    (lastParked, caught) = onTheWay;
                stoppedAt = null;
                ConfirmPending();
            }

            lastSeen = p.RecordedAt;

            if (isParked)
            {
                if (lastParked is { } from && UnseenTrip.IsCandidate(from, p))
                {
                    // Standing somewhere else than it last stood: whatever came before ended where
                    // the car stopped then, and an unseen drive brought it here.
                    segments.Close(stoppedAt);
                    ConfirmPending();
                    pending = new UnseenDrive(caught switch
                    {
                        null => [Anchored(from), Anchored(p)],
                        // Still where the poll caught it: the drive ended there.
                        _ when IsDrift(caught, p) => [Anchored(from), Anchored(caught)],
                        _ => [Anchored(from), caught, Anchored(p)],
                    });
                    stoppedAt = p;
                }
                else
                {
                    // Remember where the stop began; parked fixes are not part of the path.
                    stoppedAt ??= p;
                }

                caught = null;
                lastParked = p;
                continue;
            }

            // Moving again: a new trip if the car stood still long enough to count as parked.
            if (stoppedAt is { } stop && p.RecordedAt - stop.RecordedAt >= ParkThreshold)
            {
                segments.Close(stop);
                ConfirmPending();
            }
            else if (pending is { } brief)
            {
                // Stood too briefly to have parked: a stop on a drive that goes on. The trip leaves
                // from where the car stood before it, and the anchor decides whether it starts there.
                lastParked = brief.Points[0] with { Source = null };
                pending = null;
            }

            stoppedAt = null;
            caught = null;
            segments.Add(p, lastParked);
            lastParked = null;
        }

        // The last segment is over only when no later fix could still extend it: fixes that arrive
        // past the horizon are either a hard gap after it, or come after the car had stood still
        // long enough to count as parked.
        var stopped = lastSeen is { } seen && horizon - seen > GapThreshold ||
                      stoppedAt is { } since && horizon - since.RecordedAt >= ParkThreshold;
        DateTime? openSince = segments.OpenStart is { } start && !stopped ? start : null;

        // An open segment has not stopped for good yet, so it ends at its last moving fix.
        var wentQuiet = segments.Close(openSince is null ? stoppedAt : null);

        // A drive whose end is not settled yet is cut again next time, from its first fix after the
        // spot it left, which a caller passes back as parkedBefore.
        if (pending is { } last)
        {
            if (stopped) ConfirmPending();
            else openSince = last.Points[1].RecordedAt;
        }
        else if (wentQuiet is { Caught: var onTheWay } && horizon - onTheWay.RecordedAt <= UnseenTrip.MaxQuiet)
        {
            openSince ??= onTheWay.RecordedAt;
        }

        return new TripSegmentation(segments.Trips, openSince, segments.ParkedBefore, segments.Unseen);
    }

    private static TripPoint Anchored(TripPoint p) => p with { Source = TripPointSource.Anchored };

    private static bool IsDrift(TripPoint a, TripPoint b) =>
        GeoHelper.Haversine(a.Latitude, a.Longitude, b.Latitude, b.Longitude) * 1000 <= DriftMetres;

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
        public List<UnseenDrive> Unseen { get; } = [];

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
        /// <returns>
        /// For a segment of one moving fix after the car stood still, which went quiet without
        /// stopping: where the car stood and that fix, the drive no poll caught being on its way.
        /// </returns>
        public (TripPoint From, TripPoint Caught)? Close(TripPoint? parkedAt)
        {
            (TripPoint, TripPoint)? onTheWay = null;
            // One moving fix is no trip, but it can be the one poll that caught a drive on its way.
            if (_current is [var caught] && _currentParkedBefore is { } from)
            {
                if (parkedAt is null)
                    onTheWay = (from, caught);
                else if (UnseenTrip.IsCandidate(from, parkedAt))
                    Unseen.Add(new UnseenDrive([Anchored(from), caught, Anchored(parkedAt)]));
            }

            if (parkedAt is not null && _current.Count >= 2 && IsWorthEnding(_current[^1], parkedAt))
                _current.Add(Anchored(parkedAt));

            if (_current.Count >= 2 && BuildTrip(Trips.Count, _current) is { } trip)
            {
                Trips.Add(trip);
                ParkedBefore.Add(_currentParkedBefore);
            }

            _current.Clear();
            _currentParkedBefore = null;
            return onTheWay;
        }

        private static bool IsWorthEnding(TripPoint last, TripPoint parkedAt) =>
            !IsDrift(last, parkedAt) && IsPlausibleHop(last, parkedAt);
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
