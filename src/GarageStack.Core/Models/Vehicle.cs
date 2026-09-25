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

    public DateTime? LastParkedAt { get; set; }

    /// <summary>
    /// SAIC's id for the newest MG app message the Worker has dealt with, pushed or deliberately
    /// not. The gateway publishes its latest message again every time it starts, and this is how
    /// that repeat is told apart from a new message.
    /// </summary>
    public string? LastMessageId { get; set; }

    public ICollection<TelemetrySnapshot> TelemetrySnapshots { get; set; } = [];
}

public static class VehicleLimits
{
    // SAIC message ids are numbers of about 19 digits; anything past this is not an id.
    public const int MessageIdMaxLength = 64;
}
