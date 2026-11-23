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
}
