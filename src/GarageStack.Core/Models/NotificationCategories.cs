namespace GarageStack.Core.Models;

/// <summary>
/// The <see cref="AppNotification.Category"/> values the Worker emits. The frontend keys its
/// notification-type filter and icons on the same strings (frontend/src/utils/notificationCategories.ts),
/// so a rename here is a rename there too.
/// </summary>
public static class NotificationCategories
{
    public const string LowTyre = "low-tyre";
    public const string HighTyre = "high-tyre";
    public const string LowEv = "low-ev";
    public const string ChargingComplete = "charging-complete";
    public const string EngineStart = "engine-start";
    public const string UnlockedParked = "unlocked-parked";
    public const string DoorsOpenParked = "doors-open-parked";
    public const string WindowsOpenParked = "windows-open-parked";
    // A message from the official MG app, passed on as SAIC wrote it.
    public const string VehicleMessage = "vehicle-message";

    /// <summary>Maintenance categories carry the item id so one item's alert cannot suppress another's.</summary>
    public static string MaintenanceOverdue(int itemId) => $"maintenance-overdue-{itemId}";

    public static string MaintenanceDueSoon(int itemId) => $"maintenance-due-soon-{itemId}";
}
