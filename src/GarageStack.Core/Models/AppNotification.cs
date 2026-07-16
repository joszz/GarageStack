namespace GarageStack.Core.Models;

public class AppNotification
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // Archived: user dismissed it from the active list, but it's still visible in history
    // (e.g. excluded from the unread count, included in the default notification-list query).
    public bool IsArchived { get; set; }
    // Deleted: excluded from every query - a harder hide than archiving, with no way back via the UI.
    public bool IsDeleted { get; set; }
    public string? Category { get; set; }
    public int? VehicleId { get; set; }
}
