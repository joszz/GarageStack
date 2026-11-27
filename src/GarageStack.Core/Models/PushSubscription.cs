namespace GarageStack.Core.Models;

public class PushSubscription
{
    public int Id { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256DhKey { get; set; } = string.Empty;
    public string AuthKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The notification types this device switched off (<see cref="NotificationCategories.Groups"/>).
    /// The checklist is kept per device, so each browser's subscription carries its own.
    /// </summary>
    public List<string> ExcludedCategories { get; set; } = [];

    /// <summary>
    /// The opt-in notification types this device switched on (<see cref="NotificationCategories.OptInGroups"/>),
    /// which no device gets until it does.
    /// </summary>
    public List<string> IncludedCategories { get; set; } = [];

    /// <summary>
    /// The notification types this device switched off for one car only, by <see cref="Vehicle.Id"/>,
    /// on top of <see cref="ExcludedCategories"/>, which leaves a type out for every car. A type
    /// reaches the device when neither list holds it. Notifications that belong to no car (the MG
    /// sign-in failing) answer to <see cref="ExcludedCategories"/> alone.
    /// </summary>
    public Dictionary<int, List<string>> ExcludedCategoriesByVehicle { get; set; } = [];

    /// <summary>The types this device switched off for <paramref name="vehicleId"/> alone, or null for a notification without a car.</summary>
    public IReadOnlyCollection<string>? ExcludedCategoriesFor(int? vehicleId) =>
        vehicleId is { } id && ExcludedCategoriesByVehicle.TryGetValue(id, out var groups) ? groups : null;
}
