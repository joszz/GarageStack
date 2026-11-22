using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

// Demo mode stores positions too, over the in-memory provider, which has no bulk updates or
// deletes: hence the two branches below. Nothing fuses them there, since the demo has no Worker.
public sealed class PhonePositionRepository(AppDbContext db) : IPhonePositionRepository
{
    public async Task<int> AddAsync(int vehicleId, string trackerId, IReadOnlyList<PhonePosition> positions, CancellationToken ct)
    {
        if (positions.Count == 0) return 0;
        var added = 0;
        // The same batch twice at once (a retry while the first is still on its way): the second
        // finds the first one's rows and stores nothing.
        await db.RetryOnceOnUniqueViolationAsync(async () => added = await StoreAsync(vehicleId, trackerId, positions, ct));
        return added;
    }

    private async Task<int> StoreAsync(int vehicleId, string trackerId, IReadOnlyList<PhonePosition> positions, CancellationToken ct)
    {
        var from = positions[0].RecordedAt;
        var to = positions[^1].RecordedAt;
        var stored = (await db.PhonePositions.AsNoTracking()
            .Where(p => p.VehicleId == vehicleId && p.TrackerId == trackerId && p.RecordedAt >= from && p.RecordedAt <= to)
            .Select(p => p.RecordedAt).ToListAsync(ct)).ToHashSet();
        // Copies, so a retry adds fresh entities rather than ones the failed save left with ids.
        var added = positions.Where(p => !stored.Contains(p.RecordedAt)).Select(p => new PhonePosition
        {
            VehicleId = vehicleId,
            TrackerId = trackerId,
            RecordedAt = p.RecordedAt,
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            AccuracyM = p.AccuracyM,
            SpeedKmh = p.SpeedKmh,
        }).ToList();
        if (added.Count == 0) return 0;
        db.PhonePositions.AddRange(added);

        // A trip fused before these arrived (a phone that was out of signal) gets another pass. Only
        // one that found no phone: a fused trip already holds phone points, and an edited or hidden
        // one is the user's.
        // Only one with a position of this batch inside it, which is what makes the Worker pick it.
        var missed = db.Trips.Where(t => t.VehicleId == vehicleId && t.PhoneFusion == PhoneFusionOutcome.NoMatch &&
            t.EditedAt == null && !t.IsHidden && t.StartedAt <= to && t.EndedAt >= from &&
            db.PhonePositions.Any(p => p.VehicleId == vehicleId && p.TrackerId == trackerId &&
                p.RecordedAt >= from && p.RecordedAt <= to && p.RecordedAt >= t.StartedAt && p.RecordedAt <= t.EndedAt));
        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await missed.ExecuteUpdateAsync(s => s.SetProperty(t => t.PhoneFusion, PhoneFusionOutcome.Pending), ct);
            await transaction.CommitAsync(ct);
        }
        else
        {
            await db.SaveChangesAsync(ct);
            foreach (var trip in await missed.ToListAsync(ct)) trip.PhoneFusion = PhoneFusionOutcome.Pending;
            await db.SaveChangesAsync(ct);
        }
        db.ChangeTracker.Clear();
        return added.Count;
    }

    public async Task<IReadOnlyList<PhoneFix>> GetFixesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct) =>
        [.. (await db.PhonePositions.AsNoTracking()
            .Where(p => p.VehicleId == vehicleId && p.RecordedAt >= from && p.RecordedAt <= to)
            .OrderBy(p => p.RecordedAt).ToListAsync(ct)).Select(p => p.ToFix())];

    public async Task<IReadOnlyList<PhonePosition>> ListAsync(int vehicleId, CancellationToken ct) =>
        await db.PhonePositions.AsNoTracking().Where(p => p.VehicleId == vehicleId)
            .OrderBy(p => p.RecordedAt).ThenBy(p => p.TrackerId).ToListAsync(ct);

    public async Task<int> RemoveOlderThanAsync(DateTime before, CancellationToken ct)
    {
        var old = db.PhonePositions.Where(p => p.RecordedAt < before);
        if (db.Database.IsRelational()) return await old.ExecuteDeleteAsync(ct);
        var loaded = await old.ToListAsync(ct);
        db.PhonePositions.RemoveRange(loaded);
        await db.SaveChangesAsync(ct);
        return loaded.Count;
    }
}
