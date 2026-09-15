namespace GarageStack.Core.Interfaces;

/// <summary>
/// Delivers a notification to every subscribed browser (Web Push/VAPID) and persists an
/// <c>AppNotification</c> row so it also appears in the in-app bell, regardless of whether
/// push is configured or delivery succeeds. The <c>category</c> argument is used by callers'
/// own cooldown logic (see <c>NotificationCooldownGate</c>) to dedupe repeated alerts for the
/// same condition; <c>vehicleId</c> scopes an alert to one vehicle.
/// </summary>
public interface IPushSender
{
    Task SendToAllAsync(string title, string body, CancellationToken ct = default, string? category = null, int? vehicleId = null);
}
