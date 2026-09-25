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
            var segmentation = TripSegmenter.Segment(fixes, until);

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
                segmentation = TripSegmenter.Segment(fixes, DateTime.MaxValue);
            }

            var closed = await WithOdometerAsync(vehicleId, segmentation.Closed, ct);
            var next = segmentation.OpenSince ?? until;
            await trips.SaveRecordedAsync(vehicleId, closed, next, ct);
            saved += closed.Count;

            if (isLatest) break;
            cursor = next;
        }

        return saved;
    }

    // The start reading is the last one before the car moved off. The end reading is taken once the
    // car has been parked for as long as it takes to end a trip: the car reports its final mileage
    // a poll or two after it stops, and the next trip cannot start before that much time has passed.
    private async Task<IReadOnlyList<RecordedTrip>> WithOdometerAsync(
        int vehicleId, IReadOnlyList<TripDto> closed, CancellationToken ct)
    {
        var recorded = new List<RecordedTrip>(closed.Count);
        foreach (var trip in closed)
        {
            recorded.Add(new RecordedTrip(
                trip,
                await telemetry.GetOdometerAtAsync(vehicleId, trip.StartedAt, ct),
                await telemetry.GetOdometerAtAsync(vehicleId, trip.EndedAt + TripSegmenter.ParkThreshold, ct)));
        }

        return recorded;
    }
}
