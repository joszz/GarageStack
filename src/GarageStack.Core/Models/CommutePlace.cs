using System.Text.Json.Serialization;

namespace GarageStack.Core.Models;

/// <summary>
/// Home or a work place, for labelling trips between them as commutes (<see cref="Helpers.CommuteRule"/>).
/// Kept per car rather than per account: trips belong to the car, and the Worker labels them as
/// it saves them, with nobody signed in.
/// </summary>
public class CommutePlace
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public CommutePlaceKind Kind { get; set; }

    /// <summary>An optional name, such as "Office Utrecht".</summary>
    public string? Label { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>
    /// How far from <see cref="Latitude"/>/<see cref="Longitude"/> a trip may end and still count
    /// as here: a car parks down the street or in a garage, not on the pin.
    /// </summary>
    public int RadiusMeters { get; set; } = CommutePlaceLimits.DefaultRadiusMeters;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[JsonConverter(typeof(CommutePlaceKindJsonConverter))]
public enum CommutePlaceKind
{
    [JsonStringEnumMemberName("home")]
    Home,

    [JsonStringEnumMemberName("work")]
    Work,
}

/// <summary>Reads and writes a kind by name only, as <see cref="TripPurposeJsonConverter"/> does.</summary>
public sealed class CommutePlaceKindJsonConverter()
    : JsonStringEnumConverter<CommutePlaceKind>(namingPolicy: null, allowIntegerValues: false);

public static class CommutePlaceLimits
{
    public const int LabelMaxLength = 60;
    public const int MinRadiusMeters = 50;
    public const int MaxRadiusMeters = 1000;
    public const int DefaultRadiusMeters = 250;

    // Home, a few offices and a client or two; anything past this is not a commute any more.
    public const int MaxPerVehicle = 10;
}
