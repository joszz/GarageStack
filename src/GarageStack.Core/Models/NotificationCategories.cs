using System.Globalization;

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
    // One tyre losing air the other three keep, over days: a slow puncture.
    public const string SlowPuncture = "slow-puncture";
    public const string LowEv = "low-ev";
    public const string ChargingComplete = "charging-complete";
    // Charging stopped for a fault the car named.
    public const string ChargingInterrupted = "charging-interrupted";
    public const string EngineStart = "engine-start";
    public const string UnlockedParked = "unlocked-parked";
    public const string DoorsOpenParked = "doors-open-parked";
    public const string WindowsOpenParked = "windows-open-parked";
    // The car reported a position away from where it parked, with the engine off.
    public const string MovedParked = "moved-parked";
    // A message from the official MG app, passed on as SAIC wrote it.
    public const string VehicleMessage = "vehicle-message";
    // A climate schedule started climate, or could not.
    public const string ClimateSchedule = "climate-schedule";
    public const string Low12V = "low-12v";
    public const string LowFuel = "low-fuel";
    // The gateway could not sign in to the MG account, so no data arrives until it can.
    public const string GatewayLogin = "gateway-login";
    // The car has not reported for longer than it should.
    public const string DataStale = "data-stale";

    // What the maintenance categories below are grouped as wherever the user picks types.
    public const string Maintenance = "maintenance";

    /// <summary>Maintenance categories carry the item id so one item's alert cannot suppress another's.</summary>
    public static string MaintenanceOverdue(int itemId) => $"maintenance-overdue-{itemId}";

    public static string MaintenanceDueSoon(int itemId) => $"maintenance-due-soon-{itemId}";

    // What the RDW categories below are grouped as: the Dutch registration's APK, recalls and insurance.
    public const string Rdw = "rdw";

    /// <summary>One per recall code, so each new recall is its own notification.</summary>
    public static string RdwRecall(string code) => $"rdw-recall-{code}";

    /// <summary>
    /// The APK notice <paramref name="days"/> days ahead. The expiry date is part of it, so next
    /// year's APK, or the right car's after a mistyped plate, is not held back by this one's.
    /// </summary>
    public static string RdwApkDue(int days, DateOnly expiresOn) =>
        string.Create(CultureInfo.InvariantCulture, $"rdw-apk-due-{days}-{expiresOn:yyyyMMdd}");

    public static string RdwApkExpired(DateOnly expiresOn) =>
        string.Create(CultureInfo.InvariantCulture, $"rdw-apk-expired-{expiresOn:yyyyMMdd}");

    // The RDW has no WAM (third-party liability) insurance on record for the car.
    public const string RdwUninsured = "rdw-uninsured";

    /// <summary>The types a user can switch off, as the frontend's notification checklist offers them.</summary>
    public static readonly IReadOnlySet<string> Groups = new HashSet<string>(StringComparer.Ordinal)
    {
        LowTyre, HighTyre, SlowPuncture, LowEv, ChargingComplete, ChargingInterrupted, EngineStart, UnlockedParked, DoorsOpenParked,
        WindowsOpenParked, MovedParked, VehicleMessage, ClimateSchedule, Low12V, LowFuel, GatewayLogin, DataStale,
        Maintenance, Rdw,
    };

    /// <summary>
    /// The type <paramref name="category"/> is switched off as: itself, "maintenance" for the
    /// per-item maintenance categories, or "rdw" for the RDW ones. Mirrors notificationCategoryId
    /// in the frontend.
    /// </summary>
    public static string? GroupOf(string? category) =>
        category switch
        {
            null => null,
            _ when category.StartsWith(Maintenance + "-", StringComparison.Ordinal) => Maintenance,
            _ when category.StartsWith(Rdw + "-", StringComparison.Ordinal) => Rdw,
            _ when Groups.Contains(category) => category,
            _ => null,
        };

    /// <summary>
    /// Whether a device that switched off <paramref name="excludedGroups"/> still wants a
    /// <paramref name="category"/> notification. One without a known type always goes out: the
    /// checklist offers no way to switch it off.
    /// </summary>
    public static bool IsWanted(string? category, IReadOnlyCollection<string> excludedGroups) =>
        GroupOf(category) is not { } group || !excludedGroups.Contains(group);
}
