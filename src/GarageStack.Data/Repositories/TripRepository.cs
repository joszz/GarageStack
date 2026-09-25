using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Repositories;

// logger is optional (DI always supplies one) so tests can construct this without one.
public class TripRepository(
    AppDbContext db,
    ITelemetryRepository telemetry,
    ILogger<TripRepository>? logger = null) : ITripRepository
{
    public async Task<IReadOnlyList<TripDto>> GetTripsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        // The line is read first, and saved trips are only served from before it. The Worker
        // saves trips and moves the line in one transaction, so a save landing between these
        // reads would otherwise show its trips twice: once saved, and once cut live.
        var recordedUntil = await GetRecordedUntilAsync(vehicleId, ct);
        var liveFrom = recordedUntil is { } line && line > from ? line : from;

        var saved = liveFrom > from ? await GetSavedAsync(vehicleId, from, Min(liveFrom, to), ct) : [];

        // Everything past the line is cut from the fixes as it always was, including the trip
        // being driven and any finished trip the Worker has not got round to yet.
        var live = liveFrom < to
            ? TripSegmenter.Segment(await telemetry.GetGpsFixesAsync(vehicleId, liveFrom, to, ct), to).Trips
            : [];

        return [.. saved.Concat(live).Select((trip, i) => trip with { Index = i })];
    }

    public Task<DateTime?> GetRecordedUntilAsync(int vehicleId, CancellationToken ct = default) =>
        db.Vehicles
            .AsNoTracking()
            .Where(v => v.Id == vehicleId)
            .Select(v => v.TripsRecordedUntil)
            .FirstOrDefaultAsync(ct);

    public async Task SaveRecordedAsync(int vehicleId, IReadOnlyList<TripDto> trips, DateTime recordedUntil, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.FindAsync([vehicleId], ct);
        if (vehicle is null) return;

        db.Trips.AddRange(trips.Select(trip => new Trip
        {
            VehicleId = vehicleId,
            StartedAt = trip.StartedAt,
            EndedAt = trip.EndedAt,
            DistanceKm = trip.DistanceKm,
            PointCount = trip.PointCount,
            PointsJson = JsonSerializer.Serialize(trip.Points),
        }));
        vehicle.TripsRecordedUntil = recordedUntil;

        // One SaveChanges is one transaction: the trips and the line move together or not at all.
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<TripDto>> GetSavedAsync(int vehicleId, DateTime from, DateTime before, CancellationToken ct)
    {
        var rows = await db.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId && t.StartedAt >= from && t.StartedAt < before)
            .OrderBy(t => t.StartedAt)
            .ToListAsync(ct);

        var trips = new List<TripDto>(rows.Count);
        foreach (var row in rows)
        {
            // Every view draws a trip from its fixes, so a row whose fixes cannot be read is left
            // out rather than served as a trip that has nowhere to be drawn.
            var points = SafeJson.TryDeserialize<List<TripPoint>>(
                row.PointsJson,
                ex => logger?.LogWarning(ex, "Trip {Id} has unreadable fixes, leaving it out", row.Id));
            if (points is not { Count: > 0 }) continue;

            trips.Add(new TripDto(0, row.StartedAt, row.EndedAt, row.DistanceKm, row.PointCount, points, row.Id));
        }

        return trips;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
