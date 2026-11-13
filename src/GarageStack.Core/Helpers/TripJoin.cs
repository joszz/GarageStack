using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>One of the two trips being joined, as both trip repositories hold it.</summary>
public sealed record TripJoinPart(
    double DistanceKm,
    double StartLatitude,
    double StartLongitude,
    double EndLatitude,
    double EndLongitude,
    IReadOnlyList<TripPoint> Points,
    // The route as the car recorded it, when it was edited since; null when it was not.
    IReadOnlyList<TripPoint>? OriginalPoints,
    bool IsHidden,
    string? Notes,
    TripPurpose? Purpose,
    bool PurposeIsManual,
    PhoneFusionOutcome? PhoneFusion,
    DateTime? PhoneFusionAt,
    DateTime? EditedAt);

/// <summary>What the joined trip holds besides what it takes from the second trip unchanged.</summary>
public sealed record TripJoined(
    double DistanceKm,
    IReadOnlyList<TripPoint> Points,
    IReadOnlyList<TripPoint>? OriginalPoints,
    double? MaxSpeedKmh,
    double? AvgMovingSpeedKmh,
    int MovingSpeedSamples,
    bool IsHidden,
    string? Notes,
    TripPurpose? Purpose,
    bool PurposeIsManual,
    PhoneFusionOutcome? PhoneFusion,
    DateTime? PhoneFusionAt,
    DateTime? EditedAt);

/// <summary>
/// Joining a trip with the saved trip after it, such as two trips a fuel stop split: what the joined
/// trip is. The real repository and the demo's both take it from here, so a demo join is a real one.
/// The ends, the times and the odometer at the end are the second trip's, which each repository
/// copies itself.
/// </summary>
public static class TripJoin
{
    public static TripJoined Join(TripJoinPart first, TripJoinPart second, IReadOnlyList<CommutePlace> places)
    {
        List<TripPoint> points = [.. first.Points, .. second.Points];
        // The figures read off the fixes are worked out for the joined route as a whole.
        var figures = new TripDto(0, points[0].RecordedAt, points[^1].RecordedAt, 0, points.Count, points);

        // The distance along the fixes, as each trip counted its own, with the hop across the stop.
        var stop = GeoHelper.Haversine(
            first.Points[^1].Latitude, first.Points[^1].Longitude, second.Points[0].Latitude, second.Points[0].Longitude);

        // The joined trip as recorded is the two as recorded, so a reset undoes the edits of both
        // and keeps the join.
        var edited = first.OriginalPoints is not null || second.OriginalPoints is not null;
        IReadOnlyList<TripPoint>? original = edited
            ? [.. first.OriginalPoints ?? first.Points, .. second.OriginalPoints ?? second.Points]
            : null;

        // A purpose someone chose wins, the first trip's before the second's; otherwise it is worked
        // out again from home and work, for where the joined trip now starts and ends.
        var (purpose, manual) = first.PurposeIsManual ? (first.Purpose, true)
            : second.PurposeIsManual ? (second.Purpose, true)
            : (CommuteRule.AutomaticPurpose(
                first.StartLatitude, first.StartLongitude, second.EndLatitude, second.EndLongitude, places), false);

        return new TripJoined(
            DistanceKm: first.DistanceKm + second.DistanceKm + stop,
            Points: points,
            OriginalPoints: original,
            MaxSpeedKmh: figures.MaxSpeedKmh,
            AvgMovingSpeedKmh: figures.AvgMovingSpeedKmh,
            MovingSpeedSamples: figures.MovingSpeedSamples,
            // Joining a trip someone hid with one they did not is asking to see the whole drive.
            IsHidden: first.IsHidden && second.IsHidden,
            Notes: JoinNotes(first.Notes, second.Notes),
            Purpose: purpose,
            PurposeIsManual: manual,
            // A joined route that already contains phone data must not acquire a second track.
            PhoneFusion: first.PhoneFusion == PhoneFusionOutcome.Fused || second.PhoneFusion == PhoneFusionOutcome.Fused
                ? PhoneFusionOutcome.Fused : null,
            PhoneFusionAt: Latest(first.PhoneFusionAt, second.PhoneFusionAt),
            EditedAt: edited ? Latest(first.EditedAt, second.EditedAt) : first.EditedAt);
    }

    /// <summary>Both trips' notes, the first's first; one trip's when the other has none.</summary>
    public static string? JoinNotes(string? first, string? second) =>
        (first, second) switch
        {
            (null, _) => second,
            (_, null) => first,
            _ => $"{first} / {second}".Truncate(Trip.NotesMaxLength),
        };

    private static DateTime? Latest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
