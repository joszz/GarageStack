using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

public class HousekeepingRepository(AppDbContext db) : IHousekeepingRepository
{
    public Task<DateTime?> GetFirstTelemetryAtAsync(int vehicleId, CancellationToken ct = default) =>
        db.TelemetrySnapshots
            .AsNoTracking()
            .Where(s => s.VehicleId == vehicleId)
            .OrderBy(s => s.RecordedAt)
            .Select(s => (DateTime?)s.RecordedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<int> CompactTelemetryAsync(int vehicleId, DateTime from, DateTime until, CancellationToken ct = default)
    {
        var rows = await db.TelemetrySnapshots
            .Where(s => s.VehicleId == vehicleId && s.RecordedAt >= from && s.RecordedAt < until)
            .ToListAsync(ct);

        // The kept rows are filled in place and saved as tracked changes, together with the line.
        var folded = TelemetryCompaction.Fold(rows);
        db.TelemetrySnapshots.RemoveRange(folded);

        var vehicle = await db.Vehicles.FirstAsync(v => v.Id == vehicleId, ct);
        vehicle.TelemetryCompactedUntil = until;

        await db.SaveChangesAsync(ct);

        // A first run can work through years of history a day at a time; letting go of each day's
        // rows keeps the change tracker from holding all of them.
        db.ChangeTracker.Clear();
        return folded.Count;
    }

    public async Task<int> RemoveExpiredCacheEntriesAsync(DateTime before, CancellationToken ct = default) =>
        await DeleteAsync(db.GeocodeCacheEntries.Where(e => e.ExpiresAt < before), ct)
        + await DeleteAsync(db.MapMatchCacheEntries.Where(e => e.ExpiresAt < before), ct);

    public Task<int> RemoveDeletedNotificationsAsync(DateTime createdBefore, CancellationToken ct = default) =>
        DeleteAsync(db.AppNotifications.Where(n => n.IsDeleted && n.CreatedAt < createdBefore), ct);

    // A single DELETE on PostgreSQL; the in-memory provider the tests use cannot run bulk
    // operations, so it falls back to loading the rows into the change tracker.
    private async Task<int> DeleteAsync<T>(IQueryable<T> rows, CancellationToken ct) where T : class
    {
        if (db.Database.IsRelational())
            return await rows.ExecuteDeleteAsync(ct);

        var loaded = await rows.ToListAsync(ct);
        db.RemoveRange(loaded);
        await db.SaveChangesAsync(ct);
        return loaded.Count;
    }
}
