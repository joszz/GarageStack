namespace GarageStack.Core.Models;

/// <summary>
/// A finished trip, saved by the Worker once no later fix can extend it. Its fixes are saved with
/// it, so a trip no longer has to be cut from the raw telemetry every time it is shown. The trip
/// log adds what only the driver knows (purpose, notes) and what is worth keeping for good once
/// known (the addresses at either end).
/// </summary>
public class Trip
{
    public const int NotesMaxLength = 500;

    public long Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public double DistanceKm { get; set; }
    public int PointCount { get; set; }

    /// <summary>The trip's fixes as a JSON array of <see cref="TripPoint"/>, oldest first.</summary>
    public string PointsJson { get; set; } = "[]";

    // The first and last fix, kept beside the fixes so the trip log can list a year of trips
    // without reading every fix of every one of them.
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public double EndLatitude { get; set; }
    public double EndLongitude { get; set; }

    /// <summary>The odometer when the trip started, or null when the car had not reported one yet.</summary>
    public double? OdometerStartKm { get; set; }

    /// <summary>The odometer once the car had parked, or null when it reported none.</summary>
    public double? OdometerEndKm { get; set; }

    /// <summary>
    /// The address at either end, as a JSON <see cref="PlaceAddress"/>. Null until the trip log
    /// has looked it up. Kept here rather than in the geocode cache, which expires: a trip log
    /// kept for the tax year has to show the same address in twelve months.
    /// </summary>
    public string? StartPlaceJson { get; set; }
    public string? EndPlaceJson { get; set; }

    public TripPurpose? Purpose { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
