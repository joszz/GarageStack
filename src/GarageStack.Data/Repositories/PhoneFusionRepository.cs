using System.Data;
using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GarageStack.Data.Repositories;

public sealed class PhoneFusionRepository(
    AppDbContext db, ITelemetryRepository telemetry, ILogger<PhoneFusionRepository> logger) : IPhoneFusionRepository
{
    public async Task<IReadOnlyList<LinkedPhoneTracker>> ListAsync(int vehicleId, CancellationToken ct)
    {
        var rows = await db.VehiclePhoneTrackers.AsNoTracking().Where(t => t.VehicleId == vehicleId)
            .OrderBy(t => t.TrackerId).ToListAsync(ct);
        return rows.Select(t => new LinkedPhoneTracker(t.TrackerId, new DateTimeOffset(t.CreatedAt))).ToArray();
    }

    public async Task SetAsync(int vehicleId, IReadOnlyList<string> trackers, DateTime now, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var existing = await db.VehiclePhoneTrackers.Where(t => t.VehicleId == vehicleId).ToListAsync(ct);
        db.VehiclePhoneTrackers.RemoveRange(existing.Where(t => !trackers.Contains(t.TrackerId)));
        db.VehiclePhoneTrackers.AddRange(trackers.Except(existing.Select(t => t.TrackerId)).Select(t =>
            new VehiclePhoneTracker { VehicleId = vehicleId, TrackerId = t, CreatedAt = now }));
        if (trackers.Count == 0)
        {
            var request = await db.PhoneBackfillRequests.FindAsync([vehicleId], ct);
            if (request is not null) db.PhoneBackfillRequests.Remove(request);
            var pending = db.Trips.Where(t => t.VehicleId == vehicleId && t.PhoneFusion == PhoneFusionOutcome.Pending);
            if (db.Database.IsRelational())
                await pending.ExecuteUpdateAsync(s => s.SetProperty(t => t.PhoneFusion, PhoneFusionOutcome.NotLinked), ct);
            else
                foreach (var trip in await pending.ToListAsync(ct)) trip.PhoneFusion = PhoneFusionOutcome.NotLinked;
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task RequestBackfillAsync(int vehicleId, DateTime from, DateTime until, CancellationToken ct)
    {
        var request = await db.PhoneBackfillRequests.FindAsync([vehicleId], ct);
        if (request is null)
        {
            request = new PhoneBackfillRequest { VehicleId = vehicleId };
            db.PhoneBackfillRequests.Add(request);
        }
        request.From = from;
        request.Until = until;
        request.AfterTripId = 0;
        await db.SaveChangesAsync(ct);
    }

    public async Task QueueBackfillAsync(CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        foreach (var request in await db.PhoneBackfillRequests.OrderBy(r => r.VehicleId).Take(10).ToListAsync(ct))
        {
            var trips = await db.Trips.Where(t => t.VehicleId == request.VehicleId && t.Id > request.AfterTripId &&
                    t.StartedAt >= request.From && t.EndedAt <= request.Until && t.EditedAt == null &&
                    !t.IsHidden && t.PhoneFusion != PhoneFusionOutcome.Fused)
                .OrderBy(t => t.Id).Take(500).ToListAsync(ct);
            foreach (var trip in trips) trip.PhoneFusion = PhoneFusionOutcome.Pending;
            if (trips.Count < 500) db.PhoneBackfillRequests.Remove(request);
            else request.AfterTripId = trips[^1].Id;
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<long>> PendingAsync(DateTime before, int limit, CancellationToken ct) =>
        await db.Trips.AsNoTracking().Where(t => t.EndedAt <= before && t.EditedAt == null && !t.IsHidden &&
                (t.PhoneFusion == PhoneFusionOutcome.Pending ||
                 t.PhoneFusion == null && db.VehiclePhoneTrackers.Any(l => l.VehicleId == t.VehicleId && l.CreatedAt <= t.StartedAt)) &&
                db.VehiclePhoneTrackers.Any(l => l.VehicleId == t.VehicleId))
            .OrderBy(t => t.EndedAt).Take(limit).Select(t => t.Id).ToListAsync(ct);

    public async Task<PhoneFusionWork?> ReadWorkAsync(long id, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.EditedAt == null && !t.IsHidden &&
            (t.PhoneFusion == null || t.PhoneFusion == PhoneFusionOutcome.Pending), ct);
        if (trip is null) return null;
        var links = await db.VehiclePhoneTrackers.AsNoTracking()
            .Where(l => l.VehicleId == trip.VehicleId &&
                (trip.PhoneFusion == PhoneFusionOutcome.Pending || l.CreatedAt <= trip.StartedAt))
            .ToListAsync(ct);
        if (links.Count == 0) return null;
        List<TripPoint>? points;
        try { points = JsonSerializer.Deserialize<List<TripPoint>>(trip.PointsJson); }
        catch (JsonException)
        {
            logger.LogWarning("Phone fusion skipped unreadable trip {TripId}", id);
            await MarkUnreadableAsync(trip, ct);
            return null;
        }
        if (points is not { Count: >= 2 })
        {
            logger.LogWarning("Phone fusion skipped empty trip {TripId}", id);
            await MarkUnreadableAsync(trip, ct);
            return null;
        }
        var previous = await db.Trips.AsNoTracking().Where(t => t.VehicleId == trip.VehicleId && t.StartedAt < trip.StartedAt)
            .OrderByDescending(t => t.StartedAt).FirstOrDefaultAsync(ct);
        var parked = points[0].Source == TripPointSource.Anchored
            ? points[0]
            : await telemetry.GetLastGpsFixBeforeAsync(trip.VehicleId, trip.StartedAt, ct);
        if (parked is { Speed: > 0 } || parked is not null && previous is not null && parked.RecordedAt < previous.EndedAt)
            parked = null;
        if (parked is null && previous is not null)
            parked = new(previous.EndedAt, previous.EndLatitude, previous.EndLongitude, 0);
        if (parked is not null && points[0].Source != TripPointSource.Anchored)
        {
            // A parked fix may be days old. Only the car's departure window is a trip:
            // do not import a day's unrelated phone movements just because it began near the car.
            var firstCar = points.FirstOrDefault(p => p.Source is null);
            parked = firstCar is not null && TripAnchor.IsCandidate(parked, firstCar)
                ? parked with { RecordedAt = await telemetry.GetDepartureAsync(trip.VehicleId, parked, firstCar, ct) }
                : null;
        }
        return new(trip.Id, trip.VehicleId, trip.PointsJson, points, parked, previous?.EndedAt,
            links.Select(l => new LinkedPhoneTracker(l.TrackerId, new DateTimeOffset(l.CreatedAt))).ToArray());
    }

    private async Task MarkUnreadableAsync(Trip trip, CancellationToken ct)
    {
        db.Attach(trip);
        trip.PhoneFusion = PhoneFusionOutcome.Failed;
        trip.PhoneFusionAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task<PhoneFusionOutcome?> CompleteAsync(PhoneFusionWork work, PhoneFusionResult result, DateTime now, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        // The upstream fetch happens before this transaction. A concurrent edit, merge or unlink
        // must never be overwritten by a result calculated from an older route.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var trip = await db.Trips.SingleOrDefaultAsync(t => t.Id == work.TripId, ct);
            if (trip is null || trip.EditedAt is not null || trip.IsHidden || trip.PointsJson != work.PointsJson ||
                trip.PhoneFusion is not (null or PhoneFusionOutcome.Pending)) return null;
            var linked = await db.VehiclePhoneTrackers.Where(l => l.VehicleId == work.VehicleId).ToListAsync(ct);
            if (!work.Trackers.All(w => linked.Any(l => l.TrackerId == w.TrackerId && l.CreatedAt == w.CreatedAt.UtcDateTime)))
            {
                trip.PhoneFusion = PhoneFusionOutcome.NotLinked;
            }
            else
            {
                if (result.Outcome == PhoneFusionOutcome.Fused)
                    await new TripRepository(db, telemetry).ApplyPointsAsync(trip, result.Points, ct);
                trip.PhoneFusion = result.Outcome;
            }
            trip.PhoneFusionAt = now;
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return trip.PhoneFusion;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            logger.LogInformation("Phone fusion deferred trip {TripId} after a concurrent change", work.TripId);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            logger.LogInformation("Phone fusion deferred trip {TripId} after a concurrent change", work.TripId);
        }
        finally { db.ChangeTracker.Clear(); }
        return null;
    }
}
