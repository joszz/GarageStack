using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

public class MaintenanceRepository(AppDbContext db) : IMaintenanceRepository
{
    public async Task<IReadOnlyList<MaintenanceItem>> ListAsync(int vehicleId, CancellationToken ct = default) =>
        await db.MaintenanceItems.AsNoTracking()
            .Where(m => m.VehicleId == vehicleId)
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MaintenanceItem>> ListAllAsync(CancellationToken ct = default) =>
        await db.MaintenanceItems.AsNoTracking().ToListAsync(ct);

    public async Task<MaintenanceItem> AddAsync(MaintenanceItem item, CancellationToken ct = default)
    {
        db.MaintenanceItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task<MaintenanceItem?> UpdateAsync(
        int vehicleId, int id, Action<MaintenanceItem> change, CancellationToken ct = default)
    {
        var item = await FindAsync(vehicleId, id, ct);
        if (item is null) return null;

        change(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task<bool> DeleteAsync(int vehicleId, int id, CancellationToken ct = default)
    {
        var item = await FindAsync(vehicleId, id, ct);
        if (item is null) return false;

        db.MaintenanceItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<MaintenanceLogEntry>?> GetLogAsync(int vehicleId, int itemId, CancellationToken ct = default)
    {
        if (!await db.MaintenanceItems.AnyAsync(m => m.Id == itemId && m.VehicleId == vehicleId, ct))
            return null;

        return await db.MaintenanceLogEntries.AsNoTracking()
            .Where(l => l.MaintenanceItemId == itemId)
            .OrderByDescending(l => l.PerformedAt)
            .ToListAsync(ct);
    }

    public async Task<MaintenanceServiceLogged?> LogServiceAsync(
        int vehicleId, int itemId, MaintenanceLogEntry entry, CancellationToken ct = default)
    {
        var item = await FindAsync(vehicleId, itemId, ct);
        if (item is null) return null;

        entry.MaintenanceItemId = itemId;
        db.MaintenanceLogEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        await MoveBaselineAsync(item, ct);
        return new MaintenanceServiceLogged(item, entry);
    }

    public async Task<MaintenanceItem?> DeleteLogEntryAsync(int vehicleId, int itemId, int logId, CancellationToken ct = default)
    {
        var item = await FindAsync(vehicleId, itemId, ct);
        if (item is null) return null;

        var entry = await db.MaintenanceLogEntries.FirstOrDefaultAsync(l => l.Id == logId && l.MaintenanceItemId == itemId, ct);
        if (entry is null) return null;

        db.MaintenanceLogEntries.Remove(entry);
        await db.SaveChangesAsync(ct);

        await MoveBaselineAsync(item, ct);
        return item;
    }

    private Task<MaintenanceItem?> FindAsync(int vehicleId, int id, CancellationToken ct) =>
        db.MaintenanceItems.FirstOrDefaultAsync(m => m.Id == id && m.VehicleId == vehicleId, ct);

    // The baseline is the entry with the latest PerformedAt, not the one inserted last, so logging
    // an older service after a newer one does not move what is due backwards.
    private async Task MoveBaselineAsync(MaintenanceItem item, CancellationToken ct)
    {
        var latest = await db.MaintenanceLogEntries.AsNoTracking()
            .Where(l => l.MaintenanceItemId == item.Id)
            .OrderByDescending(l => l.PerformedAt)
            .FirstOrDefaultAsync(ct);

        item.LastServiceDate = latest?.PerformedAt;
        item.LastServiceOdometerKm = latest?.OdometerKm;
        await db.SaveChangesAsync(ct);
    }
}
