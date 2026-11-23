using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

/// <summary>
/// One demo car's trips: the finished ones, and the one it is driving right now when it is on the
/// road. Shared by the demo telemetry, whose live position is where that trip has got to (or where
/// the last one ended), and the demo trip list.
/// </summary>
internal sealed class DemoTrips
{
    private readonly DemoCar _car;

    public DemoTrips(DemoCar car)
    {
        _car = car;
        All = [.. car.BuildTrips(DateTime.UtcNow).Select(WithPhone).OrderBy(t => t.StartedAt).Select((t, i) => t with { Index = i })];
        InProgress = All.Count > 0 && All[^1].Id is null ? All[^1] : null;
        InProgressDistanceKm = InProgress is null ? 0 : PathKm(InProgress.Points);
        Log = BuildLog();
    }

    /// <summary>Every trip, oldest first, ending with the one in progress when the car is driving.</summary>
    public IReadOnlyList<TripDto> All { get; }

    /// <summary>The trip being driven now, or null while the car is parked.</summary>
    public TripDto? InProgress { get; }

    /// <summary>The road the trip in progress has covered so far.</summary>
    public double InProgressDistanceKm { get; }

    /// <summary>The finished trips as the trip log first shows them, oldest first.</summary>
    public IReadOnlyList<TripLogEntry> Log { get; }

    /// <summary>Where the car is now: partway through the trip it is driving, or where the last one ended.</summary>
    public TripPoint Position => All[^1].Points[^1];

    // Roads wind between the fixes a trip is drawn from, so the odometer covers a little more
    // ground than the straight lines through them. The demo history drives the same distance.
    internal const double RoadFactor = 1.08;

    /// <summary>
    /// What a phone in the car would have logged on a trip: a fix at each of the car's own, and one
    /// halfway to the next, which is where the car has nothing and the phone fills the gap.
    /// </summary>
    internal static IEnumerable<PhoneFix> PhoneFixes(IEnumerable<TripPoint> points, string trackerId)
    {
        TripPoint? before = null;
        foreach (var p in points.Where(p => p.Source is null))
        {
            if (before is not null)
                yield return new(before.RecordedAt + (p.RecordedAt - before.RecordedAt) / 2,
                    (before.Latitude + p.Latitude) / 2, (before.Longitude + p.Longitude) / 2, 5, p.Speed, trackerId);
            yield return new(p.RecordedAt, p.Latitude, p.Longitude, 5, p.Speed, trackerId);
            before = p;
        }
    }

    private TripDto WithPhone(TripDto trip)
    {
        if (trip.Id is not { } id || !_car.PhoneTrips.Contains(id)) return trip;
        var result = TripPhoneFusion.Apply(trip.Points, null, null, [.. PhoneFixes(trip.Points, "demo-phone")]);
        return result.Outcome == PhoneFusionOutcome.Fused
            ? trip with { Points = result.Points, PointCount = result.Points.Count }
            : throw new InvalidOperationException($"Demo trip {id} found no phone to fuse.");
    }

    // The odometer is counted back from the live reading, so the readings run on from one trip to
    // the next and end where the car is now.
    private List<TripLogEntry> BuildLog()
    {
        var finished = All.Where(t => t.Id is not null).ToList();
        var entries = new TripLogEntry[finished.Count];
        var odometer = _car.CurrentOdometerKm - InProgressDistanceKm;

        for (var i = finished.Count - 1; i >= 0; i--)
        {
            var trip = finished[i];
            var id = trip.Id!.Value;
            var start = odometer - trip.DistanceKm * RoadFactor;
            entries[i] = new TripLogEntry(
                id, trip.StartedAt, trip.EndedAt, trip.DistanceKm,
                trip.Points[0].Latitude, trip.Points[0].Longitude,
                trip.Points[^1].Latitude, trip.Points[^1].Longitude,
                Math.Round(start, 1), Math.Round(odometer, 1),
                StartPlace: null, EndPlace: null,
                _car.Purposes.TryGetValue(id, out var purpose) ? purpose : null,
                _car.Notes.GetValueOrDefault(id),
                MaxSpeedKmh: trip.MaxSpeedKmh, AvgMovingSpeedKmh: trip.AvgMovingSpeedKmh,
                CounterUsed: Math.Round(trip.DistanceKm * RoadFactor * _car.CounterPer100Km(id), 2));
            odometer = start;
        }

        return [.. entries];
    }

    private static double PathKm(IReadOnlyList<TripPoint> points)
    {
        var total = 0.0;
        for (var i = 1; i < points.Count; i++)
            total += GeoHelper.Haversine(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
        return total;
    }

    /// <summary>A trip along the given waypoints, leaving at <paramref name="start"/>; no id for the one in progress.</summary>
    internal static TripDto Trip(DateTime start, long? id, IReadOnlyList<(double Lat, double Lon, double SpeedKmh)> waypoints)
    {
        var points = new List<TripPoint>(waypoints.Count);
        var totalKm = 0.0;
        var minutesElapsed = 0.0;

        for (var i = 0; i < waypoints.Count; i++)
        {
            if (i > 0)
            {
                var seg = GeoHelper.Haversine(
                    waypoints[i - 1].Lat, waypoints[i - 1].Lon,
                    waypoints[i].Lat, waypoints[i].Lon);
                totalKm += seg;
                var avgSpeed = (waypoints[i - 1].SpeedKmh + waypoints[i].SpeedKmh) / 2.0;
                minutesElapsed += avgSpeed > 0 ? seg / avgSpeed * 60.0 : 2.0;
            }
            points.Add(new TripPoint(
                start.AddMinutes(minutesElapsed),
                waypoints[i].Lat,
                waypoints[i].Lon,
                waypoints[i].SpeedKmh));
        }

        var endedAt = points[^1].RecordedAt;
        return new TripDto(0, start, endedAt, Math.Round(totalKm, 1), points.Count, points, id);
    }

    // As the Worker saves a drive no poll caught (see UnseenTrip): a straight line from the spot the
    // car had stood at since the morning to the one it was next seen at, both anchored, leaving when
    // the car woke up, with the odometer counting the road distance.
    internal static TripDto Unseen(
        DateTime start, long id, (double Lat, double Lon) from, (double Lat, double Lon) to, TimeSpan duration)
    {
        var drive = new UnseenDrive(
        [
            new TripPoint(start.AddHours(-6), from.Lat, from.Lon, 0, TripPointSource.Anchored),
            new TripPoint(start + duration, to.Lat, to.Lon, 0, TripPointSource.Anchored),
        ]);
        var straightKm = TripAnchor.GapKm(drive.Points[0], drive.Points[1]);
        var trip = UnseenTrip.Build(0, drive, wokeAt: start, 0, straightKm * RoadFactor)
                   ?? throw new InvalidOperationException("The demo's unseen drive does not pass its own rule.");
        return trip with { Id = id };
    }
}
