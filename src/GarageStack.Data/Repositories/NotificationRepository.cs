using System.Linq.Expressions;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

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
        SetAllAsync(
            n => !n.IsDeleted && !n.IsArchived,
            s => s.SetProperty(n => n.IsArchived, true),
            n => n.IsArchived = true,
            ct);

    public Task DeleteAllAsync(CancellationToken ct = default) =>
        SetAllAsync(
            n => !n.IsDeleted,
            s => s.SetProperty(n => n.IsDeleted, true),
            n => n.IsDeleted = true,
            ct);

    // One UPDATE on PostgreSQL. The in-memory provider (demo mode, tests) cannot run that, so it
    // loads the rows and saves the change instead: <paramref name="update"/> and
    // <paramref name="apply"/> are the same change, said once for each.
    private async Task SetAllAsync(
        Expression<Func<AppNotification, bool>> which,
        Action<UpdateSettersBuilder<AppNotification>> update,
        Action<AppNotification> apply,
        CancellationToken ct)
    {
        var rows = db.AppNotifications.Where(which);
        if (db.Database.IsRelational())
        {
            await rows.ExecuteUpdateAsync(update, ct);
            return;
        }

        foreach (var notification in await rows.ToListAsync(ct))
            apply(notification);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> WasSentSinceAsync(string category, int? vehicleId, DateTime cutoff, CancellationToken ct = default) =>
        db.AppNotifications.AnyAsync(n => n.Category == category && n.VehicleId == vehicleId && n.CreatedAt > cutoff, ct);
}
