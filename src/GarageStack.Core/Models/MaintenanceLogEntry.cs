namespace GarageStack.Core.Models;

public class MaintenanceLogEntry
{
    public int Id { get; set; }
    public int MaintenanceItemId { get; set; }
    public MaintenanceItem MaintenanceItem { get; set; } = null!;

    public DateTime PerformedAt { get; set; }
    public double? OdometerKm { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// What the service cost, or null when nobody said. Kept as a plain amount: the currency is a
    /// display setting of the frontend, like the units, so a cost never changes with it.
    /// </summary>
    public decimal? Cost { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
