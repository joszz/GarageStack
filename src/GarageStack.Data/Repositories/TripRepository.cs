using System.Linq.Expressions;
using System.Runtime.CompilerServices;
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

    public async Task<IReadOnlyList<TripSummaryDto>> GetSummariesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        // The same split as GetTripsAsync, for the same reason.
        var recordedUntil = await GetRecordedUntilAsync(vehicleId, ct);
        var liveFrom = recordedUntil is { } line && line > from ? line : from;

        var saved = liveFrom > from ? await GetSavedSummariesAsync(vehicleId, from, Min(liveFrom, to), ct) : [];
        var live = liveFrom < to
            ? TripSegmenter.Segment(await telemetry.GetGpsFixesAsync(vehicleId, liveFrom, to, ct), to).Trips.Select(t => t.ToSummary())
            : [];

        return [.. saved.Concat(live).Select((trip, i) => trip with { Index = i })];
    }

    private async Task<IEnumerable<TripSummaryDto>> GetSavedSummariesAsync(int vehicleId, DateTime from, DateTime before, CancellationToken ct)
    {
        var rows = await db.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId && t.StartedAt >= from && t.StartedAt < before && !t.IsHidden)
            .OrderBy(t => t.StartedAt)
            // -1 samples marks a trip saved before its figures were stored.
            .Select(t => new TripSummaryDto(
                0, t.Id, t.StartedAt, t.EndedAt, t.DistanceKm, t.PointCount, t.EndLatitude, t.EndLongitude,
                t.MaxSpeedKmh, t.AvgMovingSpeedKmh, t.MovingSpeedSamples ?? -1))
            .ToListAsync(ct);

        // A trip saved before the figures were stored, which housekeeping has not reached yet:
        // read off its fixes as before.
        var missing = rows.Where(r => r.MovingSpeedSamples < 0).Select(r => r.Id!.Value).ToList();
        if (missing.Count == 0) return rows;

        var worked = (await db.Trips.AsNoTracking().Where(t => missing.Contains(t.Id)).ToListAsync(ct))
            .Select(ToDto)
            .OfType<TripDto>()
            .ToDictionary(t => t.Id!.Value, t => t.ToSummary());

        // A trip whose fixes cannot be read is left out, as GetTripsAsync leaves it out.
        return rows
            .Where(r => r.MovingSpeedSamples >= 0 || worked.ContainsKey(r.Id!.Value))
            .Select(r => r.MovingSpeedSamples >= 0 ? r : worked[r.Id!.Value]);
    }

    public async Task<int> FillSpeedFiguresAsync(int batchSize, CancellationToken ct = default)
    {
        var trips = await db.Trips
            .Where(t => t.MovingSpeedSamples == null)
            .OrderBy(t => t.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var trip in trips)
        {
            // Unreadable fixes give no figures, and zero samples marks the trip as done either way.
            var dto = ToDto(trip);
            trip.MaxSpeedKmh = dto?.MaxSpeedKmh;
            trip.AvgMovingSpeedKmh = dto?.AvgMovingSpeedKmh;
            trip.MovingSpeedSamples = dto?.MovingSpeedSamples ?? 0;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return trips.Count;
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
            .Where(t => t.VehicleId == vehicleId && !t.IsHidden)
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

        // A new trip between home and work is a commute from the moment it is saved.
        var places = await db.CommutePlaces.AsNoTracking().Where(p => p.VehicleId == vehicleId).ToListAsync(ct);

        db.Trips.AddRange(trips.Select(recorded =>
        {
            var trip = recorded.Trip;
            var start = trip.Points[0];
            var end = trip.Points[^1];
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
                MaxSpeedKmh = trip.MaxSpeedKmh,
                AvgMovingSpeedKmh = trip.AvgMovingSpeedKmh,
                MovingSpeedSamples = trip.MovingSpeedSamples,
                Purpose = CommuteRule.AutomaticPurpose(start.Latitude, start.Longitude, end.Latitude, end.Longitude, places),
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

        // Editing only the notes sends the purpose back as it was; that is no choice about it.
        if (trip.Purpose != purpose)
        {
            trip.Purpose = purpose;
            trip.PurposeIsManual = true;
        }
        trip.Notes = notes;
        await db.SaveChangesAsync(ct);

        return (await GetLogEntriesAsync(vehicleId, [id], ct)).SingleOrDefault();
    }

    public async Task<int> SetPurposeAsync(int vehicleId, IReadOnlyCollection<long> ids, TripPurpose? purpose, CancellationToken ct = default)
    {
        var trips = await db.Trips.Where(t => t.VehicleId == vehicleId && ids.Contains(t.Id)).ToListAsync(ct);
        foreach (var trip in trips)
        {
            trip.Purpose = purpose;
            trip.PurposeIsManual = true;
        }
        await db.SaveChangesAsync(ct);
        return trips.Count;
    }

    public async Task<CommuteRelabel> RelabelCommutesAsync(int vehicleId, IReadOnlyList<CommutePlace> places, CancellationToken ct = default)
    {
        // Ends and purposes only: reading every trip's fixes to look at two coordinates would read
        // the whole GPS history. Only the trips whose label changes are loaded in full.
        var automatic = await db.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId && !t.PurposeIsManual)
            .Select(t => new { t.Id, t.StartLatitude, t.StartLongitude, t.EndLatitude, t.EndLongitude, t.Purpose })
            .ToListAsync(ct);

        var changes = automatic
            .Select(t => (t.Id, Purpose: CommuteRule.AutomaticPurpose(t.StartLatitude, t.StartLongitude, t.EndLatitude, t.EndLongitude, places), Was: t.Purpose))
            .Where(t => t.Purpose != t.Was)
            .ToDictionary(t => t.Id, t => t.Purpose);
        if (changes.Count == 0) return new CommuteRelabel(0, 0);

        var ids = changes.Keys.ToList();
        var trips = await db.Trips.Where(t => t.VehicleId == vehicleId && ids.Contains(t.Id)).ToListAsync(ct);
        foreach (var trip in trips) trip.Purpose = changes[trip.Id];
        await db.SaveChangesAsync(ct);

        var marked = changes.Values.Count(p => p == TripPurpose.Commute);
        return new CommuteRelabel(marked, changes.Count - marked);
    }

    public async Task<TripLogEntry?> SetHiddenAsync(int vehicleId, long id, bool hidden, CancellationToken ct = default)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId, ct);
        if (trip is null) return null;

        trip.IsHidden = hidden;
        await db.SaveChangesAsync(ct);
        return (await GetLogEntriesAsync(vehicleId, [id], ct)).SingleOrDefault();
    }

    public async Task<TripMergeResult> MergeWithNextAsync(int vehicleId, long id, CancellationToken ct = default)
    {
        var first = await db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId, ct);
        if (first is null) return new(TripMergeOutcome.NotFound);

        // The very next saved trip, a hidden one included: joining past it would swallow it.
        var second = await db.Trips
            .Where(t => t.VehicleId == vehicleId && t.StartedAt > first.StartedAt)
            .OrderBy(t => t.StartedAt)
            .FirstOrDefaultAsync(ct);
        if (second is null) return new(TripMergeOutcome.NoNextTrip);

        if (ToDto(first) is not { } before || ToDto(second) is not { } after ||
            !TryReadOriginal(first, out var firstOriginal) || !TryReadOriginal(second, out var secondOriginal))
            return new(TripMergeOutcome.Unreadable);

        List<TripPoint> points = [.. before.Points, .. after.Points];
        var joined = new TripDto(0, first.StartedAt, second.EndedAt, 0, points.Count, points);
        var stop = GeoHelper.Haversine(
            before.Points[^1].Latitude, before.Points[^1].Longitude, after.Points[0].Latitude, after.Points[0].Longitude);

        // The distance along the fixes, as each trip counted its own, with the hop across the stop.
        first.DistanceKm += second.DistanceKm + stop;
        first.EndedAt = second.EndedAt;
        first.PointCount = points.Count;
        first.PointsJson = JsonSerializer.Serialize(points);
        first.EndLatitude = second.EndLatitude;
        first.EndLongitude = second.EndLongitude;
        first.OdometerEndKm = second.OdometerEndKm;
        first.EndPlaceJson = second.EndPlaceJson;
        first.MaxSpeedKmh = joined.MaxSpeedKmh;
        first.AvgMovingSpeedKmh = joined.AvgMovingSpeedKmh;
        first.MovingSpeedSamples = joined.MovingSpeedSamples;
        // Joining a trip someone hid with one they did not is asking to see the whole drive.
        first.IsHidden = first.IsHidden && second.IsHidden;
        first.Notes = JoinNotes(first.Notes, second.Notes);
        // A joined route that already contains phone data must not acquire a second track.
        first.PhoneFusion = first.PhoneFusion == PhoneFusionOutcome.Fused || second.PhoneFusion == PhoneFusionOutcome.Fused
            ? PhoneFusionOutcome.Fused : null;
        first.PhoneFusionAt = Max(first.PhoneFusionAt, second.PhoneFusionAt);

        // The joined trip as recorded is the two as recorded, so a reset undoes the edits of both
        // and keeps the join.
        if (firstOriginal is not null || secondOriginal is not null)
        {
            first.OriginalPointsJson = JsonSerializer.Serialize<List<TripPoint>>(
                [.. firstOriginal ?? before.Points, .. secondOriginal ?? after.Points]);
            first.EditedAt = Max(first.EditedAt, second.EditedAt);
        }

        if (!first.PurposeIsManual && second.PurposeIsManual)
        {
            first.Purpose = second.Purpose;
            first.PurposeIsManual = true;
        }
        else if (!first.PurposeIsManual)
        {
            var places = await db.CommutePlaces.AsNoTracking().Where(p => p.VehicleId == vehicleId).ToListAsync(ct);
            first.Purpose = CommuteRule.AutomaticPurpose(
                first.StartLatitude, first.StartLongitude, first.EndLatitude, first.EndLongitude, places);
        }

        // One SaveChanges: the second trip goes in the same transaction that extends the first.
        db.Trips.Remove(second);
        await db.SaveChangesAsync(ct);
        return new(TripMergeOutcome.Merged, (await GetLogEntriesAsync(vehicleId, [id], ct)).SingleOrDefault());
    }

    public async IAsyncEnumerable<TripRoute> GetRoutesAsync(
        int vehicleId, DateTime from, DateTime to, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rows = db.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId && t.StartedAt >= from && t.StartedAt < to)
            .OrderBy(t => t.StartedAt)
            .Select(t => new { t.Id, t.PointsJson, t.OriginalPointsJson, t.EditedAt })
            .AsAsyncEnumerable();

        await foreach (var row in rows.WithCancellation(ct))
        {
            var points = SafeJson.TryDeserialize<List<TripPoint>>(
                row.PointsJson,
                ex => logger?.LogWarning(ex, "Trip {Id} has unreadable fixes, leaving its route out", row.Id));
            if (points is not { Count: > 0 }) continue;

            var original = row.OriginalPointsJson is null
                ? null
                : SafeJson.TryDeserialize<List<TripPoint>>(
                    row.OriginalPointsJson,
                    ex => logger?.LogWarning(ex, "Trip {Id} has unreadable recorded points", row.Id));
            yield return new(row.Id, points, original, row.EditedAt);
        }
    }

    public async Task<TripEditResult> GetEditorAsync(int vehicleId, long id, CancellationToken ct = default)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId, ct);
        if (trip is null) return new(TripEditOutcome.NotFound);
        if (ToDto(trip) is not { } current || !TryReadOriginal(trip, out var original)) return new(TripEditOutcome.Unreadable);

        return new(TripEditOutcome.Ok, await EditorAsync(trip, current.Points, original, ct));
    }

    public async Task<TripEditResult> SetPointsAsync(int vehicleId, long id, IReadOnlyList<TripPointInput>? points, CancellationToken ct = default)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId, ct);
        if (trip is null) return new(TripEditOutcome.NotFound);
        if (ToDto(trip) is not { } current || !TryReadOriginal(trip, out var original)) return new(TripEditOutcome.Unreadable);

        var recorded = original ?? current.Points;
        var (previous, _, notAfter) = await NeighboursAsync(trip, ct);
        if (TripEdit.TryBuild(points, recorded, previous?.EndedAt, notAfter, out var built) is { } problem)
            return new(TripEditOutcome.Invalid, Problem: problem);

        trip.OriginalPointsJson ??= trip.PointsJson;
        trip.EditedAt = DateTime.UtcNow;
        await ApplyPointsAsync(trip, built, ct);
        await db.SaveChangesAsync(ct);

        return new(TripEditOutcome.Ok, await EditorAsync(trip, built, recorded, ct));
    }

    public async Task<TripEditResult> ResetPointsAsync(int vehicleId, long id, CancellationToken ct = default)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId, ct);
        if (trip is null) return new(TripEditOutcome.NotFound);
        if (ToDto(trip) is not { } current || !TryReadOriginal(trip, out var original)) return new(TripEditOutcome.Unreadable);

        // Nothing was edited, so it is as recorded already.
        if (original is null) return new(TripEditOutcome.Ok, await EditorAsync(trip, current.Points, null, ct));

        await ApplyPointsAsync(trip, original, ct);
        trip.OriginalPointsJson = null;
        trip.EditedAt = null;
        await db.SaveChangesAsync(ct);

        return new(TripEditOutcome.Ok, await EditorAsync(trip, original, null, ct));
    }

    // Everything read off a trip's points follows them. The odometer readings stay: the trip log
    // counts what the car's own odometer counted, whatever line is drawn on the map.
    internal async Task ApplyPointsAsync(Trip trip, IReadOnlyList<TripPoint> points, CancellationToken ct)
    {
        var dto = TripEdit.Summarize(points);
        if (TripEdit.AddressMoved(trip.StartLatitude, trip.StartLongitude, points[0])) trip.StartPlaceJson = null;
        if (TripEdit.AddressMoved(trip.EndLatitude, trip.EndLongitude, points[^1])) trip.EndPlaceJson = null;

        trip.StartedAt = dto.StartedAt;
        trip.EndedAt = dto.EndedAt;
        trip.DistanceKm = dto.DistanceKm;
        trip.PointCount = dto.PointCount;
        trip.PointsJson = JsonSerializer.Serialize(points);
        trip.StartLatitude = points[0].Latitude;
        trip.StartLongitude = points[0].Longitude;
        trip.EndLatitude = points[^1].Latitude;
        trip.EndLongitude = points[^1].Longitude;
        trip.MaxSpeedKmh = dto.MaxSpeedKmh;
        trip.AvgMovingSpeedKmh = dto.AvgMovingSpeedKmh;
        trip.MovingSpeedSamples = dto.MovingSpeedSamples;

        if (!trip.PurposeIsManual)
        {
            var places = await db.CommutePlaces.AsNoTracking().Where(p => p.VehicleId == trip.VehicleId).ToListAsync(ct);
            trip.Purpose = CommuteRule.AutomaticPurpose(
                trip.StartLatitude, trip.StartLongitude, trip.EndLatitude, trip.EndLongitude, places);
        }
    }

    private async Task<TripEditor> EditorAsync(
        Trip trip, IReadOnlyList<TripPoint> points, IReadOnlyList<TripPoint>? original, CancellationToken ct)
    {
        var (previous, next, notAfter) = await NeighboursAsync(trip, ct);
        var first = points[0];
        var last = points[^1];

        // Before: the car's last parked fix, when it reported one after the trip before ended;
        // otherwise where that trip ended.
        var fixBefore = await telemetry.GetLastGpsFixBeforeAsync(trip.VehicleId, first.RecordedAt, ct);
        var parkedBefore = fixBefore is { Speed: not > 0 } && (previous is null || fixBefore.RecordedAt >= previous.EndedAt)
            ? fixBefore
            : previous is null ? null : new TripPoint(previous.EndedAt, previous.EndLatitude, previous.EndLongitude, null);
        var previousStop = parkedBefore is null
            ? null
            : TripEdit.Departing(parkedBefore, await telemetry.GetDepartureAsync(trip.VehicleId, parkedBefore, first, ct));

        // After: the car's first fix once the trip ended, when it reported standing still there;
        // otherwise where the next trip started.
        var until = last.RecordedAt + TripSegmenter.GapThreshold;
        if (notAfter is { } limit && limit < until) until = limit;
        IReadOnlyList<TripPoint> fixesAfter = until > last.RecordedAt
            ? await telemetry.GetGpsFixesAsync(trip.VehicleId, last.RecordedAt.AddTicks(1), until, ct)
            : [];
        var nextStop = fixesAfter is [{ Speed: not > 0 } parkedAfter, ..]
            ? TripEdit.Arriving(last, parkedAfter, isParkedFix: true)
            : next is null
                ? null
                : TripEdit.Arriving(last, new TripPoint(next.StartedAt, next.StartLatitude, next.StartLongitude, null), isParkedFix: false);

        return TripEdit.Editor(trip.Id, points, original, trip.EditedAt, previousStop, nextStop, previous?.EndedAt, notAfter);
    }

    // The saved trips either side, hidden ones included, since an edit may not run into either.
    // The recording line bounds the end too: past it lie the trips not saved yet.
    private async Task<(Neighbour? Previous, Neighbour? Next, DateTime? NotAfter)> NeighboursAsync(Trip trip, CancellationToken ct)
    {
        var previous = await db.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == trip.VehicleId && t.StartedAt < trip.StartedAt && t.Id != trip.Id)
            .OrderByDescending(t => t.StartedAt)
            .Select(ToNeighbour)
            .FirstOrDefaultAsync(ct);
        var next = await db.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == trip.VehicleId && t.StartedAt > trip.StartedAt && t.Id != trip.Id)
            .OrderBy(t => t.StartedAt)
            .Select(ToNeighbour)
            .FirstOrDefaultAsync(ct);

        var line = await GetRecordedUntilAsync(trip.VehicleId, ct);
        var notAfter = next?.StartedAt;
        if (line is { } recordedUntil && (notAfter is null || recordedUntil < notAfter)) notAfter = recordedUntil;
        return (previous, next, notAfter);
    }

    private static readonly Expression<Func<Trip, Neighbour>> ToNeighbour = t => new Neighbour(
        t.StartedAt, t.EndedAt, t.StartLatitude, t.StartLongitude, t.EndLatitude, t.EndLongitude);

    private sealed record Neighbour(
        DateTime StartedAt, DateTime EndedAt,
        double StartLatitude, double StartLongitude, double EndLatitude, double EndLongitude);

    // The points as recorded: null while the trip is unedited. False when they cannot be read.
    private bool TryReadOriginal(Trip trip, out List<TripPoint>? original)
    {
        original = null;
        if (trip.OriginalPointsJson is null) return true;

        original = SafeJson.TryDeserialize<List<TripPoint>>(
            trip.OriginalPointsJson,
            ex => logger?.LogWarning(ex, "Trip {Id} has unreadable recorded points", trip.Id));
        return original is { Count: > 0 };
    }

    private static DateTime? Max(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;


    private static string? JoinNotes(string? first, string? second) =>
        (first, second) switch
        {
            (null, _) => second,
            (_, null) => first,
            _ => $"{first} / {second}".Truncate(Trip.NotesMaxLength),
        };

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
        t.StartPlaceJson, t.EndPlaceJson, t.Purpose, t.Notes, t.PurposeIsManual, t.IsHidden, t.EditedAt != null,
        t.MaxSpeedKmh, t.AvgMovingSpeedKmh, t.PhoneFusionAt);

    private sealed record LogRow(
        long Id, DateTime StartedAt, DateTime EndedAt, double DistanceKm,
        double StartLatitude, double StartLongitude, double EndLatitude, double EndLongitude,
        double? OdometerStartKm, double? OdometerEndKm,
        string? StartPlaceJson, string? EndPlaceJson, TripPurpose? Purpose, string? Notes, bool PurposeIsManual, bool IsHidden,
        bool IsEdited, double? MaxSpeedKmh, double? AvgMovingSpeedKmh, DateTime? PhoneFusionAt);

    private TripLogEntry ToEntry(LogRow r) => new(
        r.Id, r.StartedAt, r.EndedAt, r.DistanceKm,
        r.StartLatitude, r.StartLongitude, r.EndLatitude, r.EndLongitude,
        r.OdometerStartKm, r.OdometerEndKm,
        ReadPlace(r.Id, r.StartPlaceJson), ReadPlace(r.Id, r.EndPlaceJson),
        r.Purpose, r.Notes, PurposeIsAutomatic: r.Purpose is not null && !r.PurposeIsManual, IsHidden: r.IsHidden,
        IsEdited: r.IsEdited, MaxSpeedKmh: r.MaxSpeedKmh, AvgMovingSpeedKmh: r.AvgMovingSpeedKmh,
        PhoneFusionAt: r.PhoneFusionAt);

    // An unreadable place is as good as none: the trip log looks it up again.
    private PlaceAddress? ReadPlace(long tripId, string? json) =>
        SafeJson.TryDeserialize<PlaceAddress>(
            json,
            ex => logger?.LogWarning(ex, "Trip {Id} has an unreadable place, looking it up again", tripId));

    private async Task<IReadOnlyList<TripDto>> GetSavedAsync(int vehicleId, DateTime from, DateTime before, CancellationToken ct)
    {
        var rows = await db.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId && t.StartedAt >= from && t.StartedAt < before && !t.IsHidden)
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
