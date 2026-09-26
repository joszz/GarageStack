using System.Linq.Expressions;
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

    // How far back the newest unsaved trip is looked for. The Worker saves a finished trip within
    // minutes, so the fixes past the recording line only span more than this while it is down.
    private static readonly TimeSpan LatestTripLookback = TimeSpan.FromDays(7);

    public async Task<TripDto?> GetLatestAsync(int vehicleId, DateTime now, CancellationToken ct = default)
    {
        // Past the recording line lie the trip being driven and any finished trip the Worker has
        // not saved yet; both are newer than every saved trip.
        var lookbackStart = now - LatestTripLookback;
        var liveFrom = await GetRecordedUntilAsync(vehicleId, ct) is { } line && line > lookbackStart ? line : lookbackStart;
        var live = TripSegmenter.Segment(await telemetry.GetGpsFixesAsync(vehicleId, liveFrom, now, ct), now).Trips;
        if (live.Count > 0) return live[^1] with { Index = 0 };

        // Newest first, one row at a time: a saved trip whose fixes cannot be read is passed over
        // rather than reported as the latest.
        var rows = db.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId)
            .OrderByDescending(t => t.StartedAt)
            .AsAsyncEnumerable();

        await foreach (var row in rows.WithCancellation(ct))
        {
            if (ToDto(row) is { } trip) return trip;
        }

        return null;
    }

    public Task<DateTime?> GetRecordedUntilAsync(int vehicleId, CancellationToken ct = default) =>
        db.Vehicles
            .AsNoTracking()
            .Where(v => v.Id == vehicleId)
            .Select(v => v.TripsRecordedUntil)
            .FirstOrDefaultAsync(ct);

    public async Task SaveRecordedAsync(int vehicleId, IReadOnlyList<RecordedTrip> trips, DateTime recordedUntil, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.FindAsync([vehicleId], ct);
        if (vehicle is null) return;

        db.Trips.AddRange(trips.Select(recorded =>
        {
            var trip = recorded.Trip;
            return new Trip
            {
                VehicleId = vehicleId,
                StartedAt = trip.StartedAt,
                EndedAt = trip.EndedAt,
                DistanceKm = trip.DistanceKm,
                PointCount = trip.PointCount,
                PointsJson = JsonSerializer.Serialize(trip.Points),
                StartLatitude = trip.Points[0].Latitude,
                StartLongitude = trip.Points[0].Longitude,
                EndLatitude = trip.Points[^1].Latitude,
                EndLongitude = trip.Points[^1].Longitude,
                OdometerStartKm = recorded.OdometerStartKm,
                OdometerEndKm = recorded.OdometerEndKm,
            };
        }));
        vehicle.TripsRecordedUntil = recordedUntil;

        // One SaveChanges is one transaction: the trips and the line move together or not at all.
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TripLogEntry>> GetLogAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var rows = await db.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId && t.StartedAt >= from && t.StartedAt < to)
            .OrderBy(t => t.StartedAt)
            .Select(ToLogRow)
            .ToListAsync(ct);
        return [.. rows.Select(ToEntry)];
    }

    public async Task<IReadOnlyList<TripLogEntry>> GetLogEntriesAsync(int vehicleId, IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        var rows = await db.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId && ids.Contains(t.Id))
            .Select(ToLogRow)
            .ToListAsync(ct);
        return [.. rows.Select(ToEntry)];
    }

    public async Task<TripLogEntry?> SetPurposeAndNotesAsync(int vehicleId, long id, TripPurpose? purpose, string? notes, CancellationToken ct = default)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId, ct);
        if (trip is null) return null;

        trip.Purpose = purpose;
        trip.Notes = notes;
        await db.SaveChangesAsync(ct);

        return (await GetLogEntriesAsync(vehicleId, [id], ct)).SingleOrDefault();
    }

    public async Task<int> SetPurposeAsync(int vehicleId, IReadOnlyCollection<long> ids, TripPurpose? purpose, CancellationToken ct = default)
    {
        var trips = await db.Trips.Where(t => t.VehicleId == vehicleId && ids.Contains(t.Id)).ToListAsync(ct);
        foreach (var trip in trips) trip.Purpose = purpose;
        await db.SaveChangesAsync(ct);
        return trips.Count;
    }

    public async Task SavePlacesAsync(int vehicleId, IReadOnlyList<TripPlaces> places, CancellationToken ct = default)
    {
        var byId = places.ToDictionary(p => p.Id);
        var ids = byId.Keys.ToList();
        var trips = await db.Trips.Where(t => t.VehicleId == vehicleId && ids.Contains(t.Id)).ToListAsync(ct);

        foreach (var trip in trips)
        {
            var known = byId[trip.Id];
            if (known.StartPlace is not null) trip.StartPlaceJson = JsonSerializer.Serialize(known.StartPlace);
            if (known.EndPlace is not null) trip.EndPlaceJson = JsonSerializer.Serialize(known.EndPlace);
        }

        await db.SaveChangesAsync(ct);
    }

    // Everything the trip log shows, leaving out the fixes: a year of trips would otherwise read
    // a year of GPS data to show two coordinates per trip. Applied last in a query, since EF cannot
    // filter or sort on a record built through its constructor.
    private static readonly Expression<Func<Trip, LogRow>> ToLogRow = t => new LogRow(
        t.Id, t.StartedAt, t.EndedAt, t.DistanceKm,
        t.StartLatitude, t.StartLongitude, t.EndLatitude, t.EndLongitude,
        t.OdometerStartKm, t.OdometerEndKm,
        t.StartPlaceJson, t.EndPlaceJson, t.Purpose, t.Notes);

    private sealed record LogRow(
        long Id, DateTime StartedAt, DateTime EndedAt, double DistanceKm,
        double StartLatitude, double StartLongitude, double EndLatitude, double EndLongitude,
        double? OdometerStartKm, double? OdometerEndKm,
        string? StartPlaceJson, string? EndPlaceJson, TripPurpose? Purpose, string? Notes);

    private TripLogEntry ToEntry(LogRow r) => new(
        r.Id, r.StartedAt, r.EndedAt, r.DistanceKm,
        r.StartLatitude, r.StartLongitude, r.EndLatitude, r.EndLongitude,
        r.OdometerStartKm, r.OdometerEndKm,
        ReadPlace(r.Id, r.StartPlaceJson), ReadPlace(r.Id, r.EndPlaceJson),
        r.Purpose, r.Notes);

    // An unreadable place is as good as none: the trip log looks it up again.
    private PlaceAddress? ReadPlace(long tripId, string? json) =>
        SafeJson.TryDeserialize<PlaceAddress>(
            json,
            ex => logger?.LogWarning(ex, "Trip {Id} has an unreadable place, looking it up again", tripId));

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
            if (ToDto(row) is { } trip) trips.Add(trip);
        }

        return trips;
    }

    // Every view draws a trip from its fixes, so a row whose fixes cannot be read is left out
    // rather than served as a trip that has nowhere to be drawn.
    private TripDto? ToDto(Trip row)
    {
        var points = SafeJson.TryDeserialize<List<TripPoint>>(
            row.PointsJson,
            ex => logger?.LogWarning(ex, "Trip {Id} has unreadable fixes, leaving it out", row.Id));

        return points is { Count: > 0 }
            ? new TripDto(0, row.StartedAt, row.EndedAt, row.DistanceKm, row.PointCount, points, row.Id)
            : null;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
