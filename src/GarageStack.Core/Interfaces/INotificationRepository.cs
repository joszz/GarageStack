using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// The notifications behind the app's bell. Every alert the Worker sends is recorded here, so it
/// is also the history the alerts' cooldowns check, which survives a restart.
/// </summary>
public interface INotificationRepository
{
    /// <summary>Notifications not deleted, newest first.</summary>
    Task<IReadOnlyList<AppNotification>> ListAsync(int limit, CancellationToken ct = default);

    Task AddAsync(AppNotification notification, CancellationToken ct = default);

    /// <summary>What the app's badge shows: notifications neither archived nor deleted.</summary>
    Task<int> CountUnreadAsync(CancellationToken ct = default);

    /// <returns>false when there is no such notification, or it was deleted.</returns>
    Task<bool> ArchiveAsync(int id, CancellationToken ct = default);

    /// <summary>Marks the notification deleted; housekeeping removes it for good later.</summary>
    /// <returns>false when there is no such notification.</returns>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    Task ArchiveAllAsync(CancellationToken ct = default);

    Task DeleteAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether a notification of <paramref name="category"/> about <paramref name="vehicleId"/>
    /// (null for one about the MG account rather than a car) was created after
    /// <paramref name="cutoff"/>: the database half of <c>NotificationCooldownGate</c>, so a
    /// restart or another service's alert still holds back a repeat.
    /// </summary>
    Task<bool> WasSentSinceAsync(string category, int? vehicleId, DateTime cutoff, CancellationToken ct = default);
}
