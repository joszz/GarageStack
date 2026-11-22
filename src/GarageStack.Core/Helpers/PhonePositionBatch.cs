using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Checks a batch of positions from the app before it is stored. A malformed position refuses the
/// whole batch, since only a broken or foreign client sends one. A position outside the time
/// window is left out quietly: a phone whose clock is off, or a queue that waited too long for a
/// signal, should not hold up the rest.
/// </summary>
public static class PhonePositionBatch
{
    public const int MaxPositions = 500;
    public const int MinTrackerLength = 8;
    public const double MaxAccuracyMetres = 10_000;
    public const double MaxSpeedKmh = 500;
    /// <summary>Older positions could only reach trips the Worker has long finished with.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    /// <summary>A phone clock a little ahead of the server's is still believed.</summary>
    public static readonly TimeSpan MaxAhead = TimeSpan.FromMinutes(5);

    public static bool IsTrackerId(string? id) =>
        id is { Length: >= MinTrackerLength and <= PhonePosition.MaxTrackerLength } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>
    /// The positions to store, one per moment, oldest first; or null when the batch is refused.
    /// </summary>
    public static IReadOnlyList<PhonePosition>? Parse(
        int vehicleId, string trackerId, IReadOnlyList<PhonePositionInput>? positions, DateTime now)
    {
        if (positions is null || positions.Count > MaxPositions || positions.Any(p => !IsValid(p))) return null;
        return [.. positions
            .Select(p => p with { RecordedAt = p.RecordedAt.ToUniversalTime() })
            .Where(p => p.RecordedAt.UtcDateTime >= now - MaxAge && p.RecordedAt.UtcDateTime <= now + MaxAhead)
            .DistinctBy(p => p.RecordedAt)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new PhonePosition
            {
                VehicleId = vehicleId,
                TrackerId = trackerId,
                RecordedAt = p.RecordedAt.UtcDateTime,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                AccuracyM = p.AccuracyM,
                SpeedKmh = p.SpeedKmh,
            })];
    }

    private static bool IsValid(PhonePositionInput p) =>
        double.IsFinite(p.Latitude) && p.Latitude is >= -90 and <= 90 &&
        double.IsFinite(p.Longitude) && p.Longitude is >= -180 and <= 180 &&
        p.AccuracyM is null or (>= 0 and <= MaxAccuracyMetres) &&
        p.SpeedKmh is null or (>= 0 and <= MaxSpeedKmh);
}
