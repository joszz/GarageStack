namespace GarageStack.Core.Models;

/// <summary>
/// A finished trip, saved by the Worker once no later fix can extend it. Its fixes are saved with
/// it, so a trip no longer has to be cut from the raw telemetry every time it is shown.
/// </summary>
public class Trip
{
    public long Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public double DistanceKm { get; set; }
    public int PointCount { get; set; }

    /// <summary>The trip's fixes as a JSON array of <see cref="TripPoint"/>, oldest first.</summary>
    public string PointsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
