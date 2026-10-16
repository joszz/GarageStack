using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

public class NotificationRepository(AppDbContext db) : INotificationRepository
{
    public async Task<IReadOnlyList<AppNotification>> ListAsync(int limit, CancellationToken ct = default) =>
        await db.AppNotifications.AsNoTracking()
            .Where(n => !n.IsDeleted)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task AddAsync(AppNotification notification, CancellationToken ct = default)
    {
        db.AppNotifications.Add(notification);
        await db.SaveChangesAsync(ct);
    }

    public Task<int> CountUnreadAsync(CancellationToken ct = default) =>
        db.AppNotifications.CountAsync(n => !n.IsArchived && !n.IsDeleted, ct);

    public async Task<bool> ArchiveAsync(int id, CancellationToken ct = default)
    {
        var notification = await db.AppNotifications.FindAsync([id], ct);
        if (notification is null || notification.IsDeleted) return false;

        notification.IsArchived = true;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var notification = await db.AppNotifications.FindAsync([id], ct);
        if (notification is null) return false;

        notification.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task ArchiveAllAsync(CancellationToken ct = default) =>
        db.AppNotifications
            .Where(n => !n.IsDeleted && !n.IsArchived)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsArchived, true), ct);

    public Task DeleteAllAsync(CancellationToken ct = default) =>
        db.AppNotifications
            .Where(n => !n.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, true), ct);

    public Task<bool> WasSentSinceAsync(string category, int? vehicleId, DateTime cutoff, CancellationToken ct = default) =>
        db.AppNotifications.AnyAsync(n => n.Category == category && n.VehicleId == vehicleId && n.CreatedAt > cutoff, ct);
}
