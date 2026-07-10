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
