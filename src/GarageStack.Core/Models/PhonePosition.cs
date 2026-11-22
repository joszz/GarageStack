namespace GarageStack.Core.Models;

/// <summary>
/// A position the installed GarageStack app sent from a phone while it was open on screen, kept
/// for <see cref="PhonePosition.KeptFor"/> so the Worker can add it to the car's trips.
/// </summary>
public class PhonePosition
{
    /// <summary>The app's own trackers count apart from Dawarich's, whose ids are free text.</summary>
    public const string TrackerPrefix = "app:";
    public const int MaxTrackerLength = 64;
    /// <summary>Long enough for a trip to be fused and fused again after a late upload.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(30);

    public long Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    /// <summary>The random id the app made for itself on that device, without the prefix.</summary>
    public string TrackerId { get; set; } = "";
    public DateTime RecordedAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AccuracyM { get; set; }
    public double? SpeedKmh { get; set; }

    public PhoneFix ToFix() =>
        new(RecordedAt, Latitude, Longitude, AccuracyM, SpeedKmh, TrackerPrefix + TrackerId);
}

/// <summary>One position as the app sends it.</summary>
/// <param name="RecordedAt">When the phone took the fix.</param>
/// <param name="Latitude">Degrees, WGS 84.</param>
/// <param name="Longitude">Degrees, WGS 84.</param>
/// <param name="AccuracyM">The phone's stated horizontal accuracy in metres.</param>
/// <param name="SpeedKmh">The phone's own speed, in km/h, when it gave one.</param>
public sealed record PhonePositionInput(
    DateTimeOffset RecordedAt, double Latitude, double Longitude, double? AccuracyM, double? SpeedKmh);
