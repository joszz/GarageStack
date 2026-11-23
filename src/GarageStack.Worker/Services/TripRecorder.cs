using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Worker.Services;

/// <summary>
/// Saves a vehicle's finished trips. It walks the fixes from the vehicle's recording line onwards a
/// week at a time, saves every trip no later fix can change, and moves the line up to the first fix
/// that could still belong to an unfinished trip. On an install that has never recorded, the line
/// starts at the vehicle's first fix, so the whole history is saved on the first run.
/// </summary>
public class TripRecorder(ITelemetryRepository telemetry, ITripRepository trips, ILogger logger)
{
    // Bounds the fixes held in memory while a long history is saved: a week of fixes is a few
    // thousand rows, where a year at once could be far more.
    internal static readonly TimeSpan Chunk = TimeSpan.FromDays(7);

    // The fixes of one poll are merged into a single row for up to 15 seconds after the time it
    // carries, so a fix can land in the table with a time that is already in the past. Fixes newer
    // than this are left for the next run rather than judged before they are all in.
    internal static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(1);

    /// <returns>How many trips were saved.</returns>
    public async Task<int> RecordAsync(int vehicleId, DateTime now, CancellationToken ct)
    {
        var from = await trips.GetRecordedUntilAsync(vehicleId, ct)
                   ?? await telemetry.GetFirstGpsFixAtAsync(vehicleId, ct);
        if (from is null) return 0;

        var settled = now - SettleTime;
        var saved = 0;
        var cursor = from.Value;

        while (cursor < settled)
        {
            var until = cursor + Chunk < settled ? cursor + Chunk : settled;
            var isLatest = until == settled;
            var fixes = await telemetry.GetGpsFixesAsync(vehicleId, cursor, until, ct);
            var parkedBefore = fixes.Count == 0 ? null : await ParkedBeforeAsync(vehicleId, cursor, ct);
            var segmentation = TripSegmenter.Segment(fixes, until, parkedBefore);

            if (segmentation.OpenSince == cursor)
            {
                // The trip being driven: leave the line where it is and look again next run.
                if (isLatest) break;

                // A segment still open after a whole week of history has neither stopped for five
                // minutes nor gone quiet for half an hour. That is a stuck sensor, not a drive, and
                // waiting for it to end would stall the line here forever, so it is cut at the week.
                logger.LogWarning(
                    "Trip for vehicleId={VehicleId} starting {Start:o} did not end within {Days} days; saving it cut at {End:o}",
                    vehicleId, cursor, Chunk.TotalDays, until);
                segmentation = TripSegmenter.Segment(fixes, DateTime.MaxValue, parkedBefore);
            }

            var closed = await CompleteAsync(vehicleId, segmentation, ct);
            var next = segmentation.OpenSince ?? until;
            await trips.SaveRecordedAsync(vehicleId, closed, next, ct);
            saved += closed.Count;

            if (isLatest) break;
            cursor = next;
        }

        return saved;
    }

    // Each closed trip, started where the car was parked when that checks out, and each unseen drive
    // the odometer confirms, with their odometer readings. The start reading is the last one before
    // the car moved off, so it is taken at the corrected start. The end reading is taken once the car
    // has been parked for as long as it takes to end a trip: the car reports its final mileage a poll
    // or two after it stops, and the next trip cannot start before that much time has passed.
    private async Task<IReadOnlyList<RecordedTrip>> CompleteAsync(
        int vehicleId, TripSegmentation segmentation, CancellationToken ct)
    {
        var recorded = new List<RecordedTrip>(segmentation.Closed.Count + segmentation.Unseen.Count);
        foreach (var cut in segmentation.Closed)
        {
            var parked = segmentation.ParkedBefore[cut.Index];
            var anchored = parked is null ? null : await AnchorAsync(vehicleId, cut, parked, ct);
            var trip = anchored?.Trip ?? cut;
            recorded.Add(await WithCounterUseAsync(vehicleId, new RecordedTrip(
                trip,
                anchored is { } a ? a.OdometerStartKm : await telemetry.GetOdometerAtAsync(vehicleId, trip.StartedAt, ct),
                await telemetry.GetOdometerAtAsync(vehicleId, trip.EndedAt + TripSegmenter.ParkThreshold, ct)), ct));
        }

        // Numbered on from the trips, so every index in the run is its own.
        var index = segmentation.Trips.Count;
        foreach (var drive in segmentation.Unseen)
        {
            if (await UnseenAsync(vehicleId, index, drive, ct) is { } unseen)
            {
                recorded.Add(unseen);
                index++;
            }
        }

        return recorded;
    }

    // The fix before the week's fixes, when the car stood still in it by the segmenter's own rule,
    // which a fix without a speed needs the fix before it for: a trip or an unseen drive at the start
    // of the week left from there, which the segmenter would otherwise never see.
    private async Task<TripPoint?> ParkedBeforeAsync(int vehicleId, DateTime cursor, CancellationToken ct)
    {
        if (await telemetry.GetLastGpsFixBeforeAsync(vehicleId, cursor, ct) is not { } last) return null;
        var previous = last.Speed is null ? await telemetry.GetLastGpsFixBeforeAsync(vehicleId, last.RecordedAt, ct) : null;
        return TripSegmenter.IsParked(last, previous) ? last : null;
    }

    // The odometer as the car stood at the drive's first spot: that poll lands its mileage within
    // the settle time, though never past the drive's next fix. And as it stood at the last, read as a
    // trip's end is: the spot is a parking, so nothing was driven in between.
    private async Task<RecordedTrip?> UnseenAsync(int vehicleId, int index, UnseenDrive drive, CancellationToken ct)
    {
        var from = drive.Points[0];
        var next = drive.Points[1];
        var beforeAt = from.RecordedAt + SettleTime < next.RecordedAt ? from.RecordedAt + SettleTime : next.RecordedAt.AddTicks(-1);
        var before = await telemetry.GetOdometerAtAsync(vehicleId, beforeAt, ct);
        var after = await telemetry.GetOdometerAtAsync(vehicleId, drive.Points[^1].RecordedAt + TripSegmenter.ParkThreshold, ct);
        var reports = await telemetry.GetReportTimesAsync(vehicleId, from.RecordedAt, next.RecordedAt, TripAnchor.WakeUpReportLimit, ct);
        var wokeAt = TripAnchor.WakeUp(from, next, reports, complete: reports.Count < TripAnchor.WakeUpReportLimit);

        var trip = UnseenTrip.Build(index, drive, wokeAt, before, after);
        if (trip is null)
        {
            logger.LogDebug(
                "Vehicle {VehicleId} seen {Gap:F2} km from where it stood, at {Seen:o}, but the odometer ({Before} to {After} km) does not confirm a drive",
                vehicleId, TripAnchor.GapKm(from, drive.Points[^1]), drive.Points[^1].RecordedAt, before, after);
            return null;
        }

        logger.LogInformation(
            "Vehicle {VehicleId} drove {Distance:F2} km unseen, from {Departure:o} to {Seen:o}; saved as a trip",
            vehicleId, trip.DistanceKm, trip.StartedAt, trip.EndedAt);
        return await WithCounterUseAsync(vehicleId, new RecordedTrip(trip, before, after), ct);
    }

    // What the day counter rose by over the trip, read up to when its end odometer is: the car
    // reports its final counter a poll or two after it stops, with its final mileage.
    private async Task<RecordedTrip> WithCounterUseAsync(int vehicleId, RecordedTrip recorded, CancellationToken ct)
    {
        var trip = recorded.Trip;
        var readings = await telemetry.GetDayCounterReadingsAsync(
            vehicleId, trip.StartedAt, trip.EndedAt + TripSegmenter.ParkThreshold, ct);
        return recorded with { CounterUsed = TripCounterUse.Rise(readings), CounterReadings = readings.Count };
    }

    // The trip started at its parking spot, with the odometer read there, or null when it should not.
    private async Task<(TripDto Trip, double? OdometerStartKm)?> AnchorAsync(int vehicleId, TripDto trip, TripPoint parked, CancellationToken ct)
    {
        var first = trip.Points[0];
        if (!TripAnchor.IsCandidate(parked, first)) return null;

        var departure = await telemetry.GetDepartureAsync(vehicleId, parked, first, ct);

        // The first fix's poll lands its mileage within the settle time, often in a row of its own.
        var before = await telemetry.GetOdometerAtAsync(vehicleId, departure, ct);
        var after = await telemetry.GetOdometerAtAsync(vehicleId, first.RecordedAt + SettleTime, ct);
        var gapKm = TripAnchor.GapKm(parked, first);

        var anchored = TripAnchor.Apply(trip, parked, departure, before, after);
        if (anchored is null)
        {
            logger.LogDebug(
                "Trip for vehicleId={VehicleId} starting {Start:o} not anchored to the parking spot {Gap:F2} km away; odometer {Before} to {After} km",
                vehicleId, trip.StartedAt, gapKm, before, after);
            return null;
        }

        logger.LogInformation(
            "Trip for vehicleId={VehicleId} starting {Start:o} now starts at its parking spot {Gap:F2} km away, from {Departure:o}",
            vehicleId, trip.StartedAt, gapKm, departure);
        return (anchored, before);
    }
}
