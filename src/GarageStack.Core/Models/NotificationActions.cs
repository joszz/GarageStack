namespace GarageStack.Core.Models;

/// <summary>
/// The buttons a push notification carries, by its category: what can be done about it straight
/// from the notification, without opening the app. The service worker (frontend/src/sw.ts) knows
/// each action by the same name and carries it out through the API; a browser without
/// notification buttons (Safari on iOS) shows the notification as before.
/// </summary>
public static class NotificationActions
{
    /// <summary>Locks the car, on "car left unlocked".</summary>
    public const string Lock = "lock";

    /// <summary>
    /// Logs the maintenance item as serviced today, at the odometer the car reports when the button
    /// is pressed, on a due-soon or overdue reminder.
    /// </summary>
    public const string MaintenanceDone = "maintenance-done";

    /// <summary>The actions a notification of <paramref name="category"/> offers, none for most.</summary>
    public static IReadOnlyList<string> For(string? category) =>
        category == NotificationCategories.UnlockedParked ? [Lock]
        : NotificationCategories.MaintenanceItemId(category) is not null ? [MaintenanceDone]
        : [];
}
