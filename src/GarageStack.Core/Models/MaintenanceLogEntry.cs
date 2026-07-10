namespace GarageStack.Core.Models;

public class MaintenanceLogEntry
{
    public int Id { get; set; }
    public int MaintenanceItemId { get; set; }
    public MaintenanceItem MaintenanceItem { get; set; } = null!;

    public DateTime PerformedAt { get; set; }
    public double? OdometerKm { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
