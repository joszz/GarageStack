using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Extensions;

public static class NotificationHistoryExtensions
{
    /// <summary>
    /// Checks whether an <c>AppNotification</c> matching <paramref name="category"/> and
    /// <paramref name="vehicleId"/> was created after <paramref name="cutoff"/> - the standard
    /// "is this a duplicate" query behind <c>NotificationCooldownGate.ShouldNotifyAsync</c>'s
    /// DB fallback, kept here in one place so both notification-check services query it the
    /// same way.
    /// </summary>
    public static Task<bool> WasNotificationSentSinceAsync(
        this AppDbContext db, string category, int vehicleId, DateTime cutoff, CancellationToken ct = default) =>
        db.AppNotifications.AnyAsync(n => n.Category == category && n.VehicleId == vehicleId && n.CreatedAt > cutoff, ct);
}
