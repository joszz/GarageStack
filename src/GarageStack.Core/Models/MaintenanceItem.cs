namespace GarageStack.Core.Models;

public class MaintenanceItem
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public double? IntervalKm { get; set; }
    public int? IntervalMonths { get; set; }

    public DateTime? LastServiceDate { get; set; }
    public double? LastServiceOdometerKm { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MaintenanceLogEntry> LogEntries { get; set; } = [];
}

/// <summary>Column limits shared between the model configuration and the endpoint's validation.</summary>
public static class MaintenanceLimits
{
    public const int NameMaxLength = 200;

    /// <summary>For an item's notes and for a logged service's notes alike.</summary>
    public const int NotesMaxLength = 1000;
}
