using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

internal static class DemoCapturedTrips
{
    private static readonly Lazy<IReadOnlyList<TripDto>> _all = new(BuildTrips, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<TripDto> All => _all.Value;

    private static readonly Lazy<IReadOnlyList<TripLogEntry>> _log = new(BuildLog, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<TripLogEntry> Log => _log.Value;

    private static IReadOnlyList<TripDto> BuildTrips()
    {
        var snapshots = DemoTelemetryCapture.Snapshots;
        if (snapshots is not { Count: > 0 }) return [];

        var fixes = snapshots
            .Where(snapshot => snapshot.Latitude is { } latitude && double.IsFinite(latitude) && latitude is >= -90 and <= 90 &&
                               snapshot.Longitude is { } longitude && double.IsFinite(longitude) && longitude is >= -180 and <= 180)
            .Select(snapshot => new TripPoint(
                snapshot.RecordedAt, snapshot.Latitude!.Value, snapshot.Longitude!.Value, snapshot.Speed))
            .ToArray();
        if (fixes.Length == 0) return [];

        var horizon = snapshots[^1].RecordedAt;
        var segmentation = TripSegmenter.Segment(fixes, horizon);
        var trips = segmentation.Trips
            .Select(trip => (
                Trip: trip,
                IsOpen: segmentation.OpenSince is { } openSince && trip.StartedAt >= openSince))
            .ToList();

        foreach (var drive in segmentation.Unseen)
        {
            var start = drive.Points[0];
            var end = drive.Points[^1];
            var reports = snapshots
                .Where(snapshot => snapshot.RecordedAt > start.RecordedAt && snapshot.RecordedAt < end.RecordedAt)
                .OrderByDescending(snapshot => snapshot.RecordedAt)
                .Take(TripAnchor.WakeUpReportLimit)
                .Select(snapshot => snapshot.RecordedAt)
                .ToArray();
            var wokeAt = TripAnchor.WakeUp(start, end, reports, reports.Length < TripAnchor.WakeUpReportLimit);
            var unseen = UnseenTrip.Build(
                trips.Count,
                drive,
                wokeAt,
                OdometerAt(snapshots, start.RecordedAt),
                OdometerAt(snapshots, end.RecordedAt));
            if (unseen is not null) trips.Add((unseen, IsOpen: false));
        }

        var nextId = 1L;
        return trips
            .OrderBy(item => item.Trip.StartedAt)
            .ThenBy(item => item.Trip.EndedAt)
            .Select((item, index) => item.Trip with
            {
                Index = index,
                Id = item.IsOpen ? null : nextId++,
            })
            .ToArray();
    }

    private static IReadOnlyList<TripLogEntry> BuildLog()
    {
        var snapshots = DemoTelemetryCapture.Snapshots;
        if (snapshots is not { Count: > 0 }) return [];

        return All
            .Where(trip => trip.Id is not null)
            .Select(trip => new TripLogEntry(
                trip.Id!.Value,
                trip.StartedAt,
                trip.EndedAt,
                trip.DistanceKm,
                trip.Points[0].Latitude,
                trip.Points[0].Longitude,
                trip.Points[^1].Latitude,
                trip.Points[^1].Longitude,
                OdometerAt(snapshots, trip.StartedAt),
                OdometerAt(snapshots, trip.EndedAt),
                StartPlace: null,
                EndPlace: null,
                Purpose: null,
                Notes: null,
                MaxSpeedKmh: trip.MaxSpeedKmh,
                AvgMovingSpeedKmh: trip.AvgMovingSpeedKmh))
            .ToArray();
    }

    private static double? OdometerAt(IReadOnlyList<TelemetrySnapshot> snapshots, DateTime at) =>
        snapshots
            .Where(snapshot => snapshot.RecordedAt <= at && snapshot.OdometerKm is not null)
            .OrderByDescending(snapshot => snapshot.RecordedAt)
            .Select(snapshot => snapshot.OdometerKm)
            .FirstOrDefault();
}
