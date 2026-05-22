namespace GarageStack.Core.Models;

public class Vehicle
{
    public int Id { get; set; }
    public string Vin { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? Series { get; set; }
    public string? SaicUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>JSON blob of capability flags from info/configuration/* MQTT messages.</summary>
    public string? ConfigJson { get; set; }

    public ICollection<TelemetrySnapshot> TelemetrySnapshots { get; set; } = [];
}
