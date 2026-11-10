namespace GarageStack.Core.Models;

public sealed record PhoneFix(
    DateTime RecordedAt, double Latitude, double Longitude, double? AccuracyM, double? SpeedKmh, string? TrackerId);

/// <summary>A tracker observed in Dawarich, without any of its positions.</summary>
public sealed record PhoneTracker(string TrackerId, DateTimeOffset LastSeenAt);

public enum PhoneFusionOutcome { Pending, Fused, NoMatch, NotLinked, Failed }

public sealed record PhoneFusionResult(IReadOnlyList<TripPoint> Points, PhoneFusionOutcome Outcome);

public class VehiclePhoneTracker
{
    public const int MaxTrackerLength = 200;
    public const int MaxTrackers = 10;
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public string TrackerId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class PhoneBackfillRequest
{
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public DateTime From { get; set; }
    public DateTime Until { get; set; }
    public long AfterTripId { get; set; }
}
