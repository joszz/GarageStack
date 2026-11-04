using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Decides whether a trip should start where the car was parked before it. A car that sleeps while
/// parked often reports its first fix of the next drive some way down the road, so the trip as
/// recorded starts there and the stretch from the parking spot is missing. The spot the car last
/// reported standing still at is added as the first point when the gap is real driving: far enough
/// to be more than drift, near enough to be the same drive, and confirmed by the odometer, which
/// rules out a ferry, a tow truck or a GPS jump.
/// </summary>
public static class TripAnchor
{
    /// <summary>A parked spot nearer than this to the first fix is where the trip already starts.</summary>
    public const double MinGapMetres = 150;

    /// <summary>A parked spot further than this is a different story than one missed stretch.</summary>
    public const double MaxGapKm = 30;

    /// <summary>
    /// How fast the car is taken to have covered the gap when nothing says when it left. Town
    /// speed, on purpose: the drive away from a parking spot is rarely fast.
    /// </summary>
    public const double AssumedSpeedKmh = 25;

    /// <summary>
    /// A wake-up this soon before the first fix that the gap implies a faster average than this
    /// was reported mid-drive, not when the car left.
    /// </summary>
    public const double MaxWakeUpSpeedKmh = 130;

    /// <summary>The odometer must count at least this share of the straight-line gap.</summary>
    public const double MinOdometerRatio = 0.8;

    /// <summary>
    /// And at most this multiple of it: more means the car went somewhere else first, and a
    /// straight line from the parking spot would draw a drive that did not happen.
    /// </summary>
    public const double MaxOdometerRatio = 3;

    /// <summary>The odometer reports whole kilometres, so a difference is out by up to this either way.</summary>
    public const double OdometerResolutionKm = 1;

    /// <summary>
    /// How many reports before a trip's first fix are read to find when the car woke up. A parked
    /// car reports a handful of polls before it sleeps, so a quiet period is found well within this.
    /// </summary>
    public const int WakeUpReportLimit = 200;

    /// <summary>
    /// When the car left <paramref name="parked"/> for the trip starting at <paramref name="first"/>:
    /// <see cref="Departure"/> with the wake-up read from the reports in between.
    /// </summary>
    public static async Task<DateTime> GetDepartureAsync(
        this ITelemetryRepository telemetry, int vehicleId, TripPoint parked, TripPoint first, CancellationToken ct = default)
    {
        var reports = await telemetry.GetReportTimesAsync(vehicleId, parked.RecordedAt, first.RecordedAt, WakeUpReportLimit, ct);
        return Departure(parked, first, WakeUp(parked, first, reports, complete: reports.Count < WakeUpReportLimit));
    }

    /// <summary>The straight-line distance from the parked spot to the trip's first fix.</summary>
    public static double GapKm(TripPoint parked, TripPoint first) =>
        GeoHelper.Haversine(parked.Latitude, parked.Longitude, first.Latitude, first.Longitude);

    /// <summary>
    /// Whether the parked spot is far enough from the first fix, and near enough, to be worth the
    /// odometer readings that decide it.
    /// </summary>
    public static bool IsCandidate(TripPoint parked, TripPoint first)
    {
        var gapKm = GapKm(parked, first);
        return parked.RecordedAt < first.RecordedAt && gapKm * 1000 >= MinGapMetres && gapKm <= MaxGapKm;
    }

    /// <summary>
    /// When the car woke up for the drive: the first telemetry of any kind after the last quiet
    /// period (<see cref="TripSegmenter.GapThreshold"/> without a report) before the first fix.
    /// Null when there is no such report: the first fix itself ended the quiet, or the car never
    /// went quiet after it parked, or <paramref name="complete"/> is false and the quiet lies
    /// beyond the reports read.
    /// </summary>
    /// <param name="parked">The last fix the car reported standing still.</param>
    /// <param name="first">The trip's first fix.</param>
    /// <param name="newestFirst">The times of every report between the parked fix and the first fix, newest first.</param>
    /// <param name="complete">False when the reports were capped, so older ones are missing.</param>
    public static DateTime? WakeUp(TripPoint parked, TripPoint first, IReadOnlyList<DateTime> newestFirst, bool complete)
    {
        var after = first.RecordedAt;
        foreach (var at in newestFirst)
        {
            if (after - at > TripSegmenter.GapThreshold) return WokeAt(after);
            after = at;
        }

        return complete && after - parked.RecordedAt > TripSegmenter.GapThreshold ? WokeAt(after) : null;

        DateTime? WokeAt(DateTime at) => at < first.RecordedAt ? at : null;
    }

    /// <summary>
    /// When the car left the parked spot. The parked fix may be hours old, so its own time is no
    /// use: the car's wake-up when there is a believable one, otherwise the first fix's time less
    /// the gap at <see cref="AssumedSpeedKmh"/>. Never earlier than the parked fix.
    /// </summary>
    public static DateTime Departure(TripPoint parked, TripPoint first, DateTime? wokeAt)
    {
        var gapKm = GapKm(parked, first);
        if (wokeAt is { } woke && woke > parked.RecordedAt && woke < first.RecordedAt &&
            gapKm / (first.RecordedAt - woke).TotalHours <= MaxWakeUpSpeedKmh)
            return woke;

        var estimate = first.RecordedAt - TimeSpan.FromHours(gapKm / AssumedSpeedKmh);
        return estimate > parked.RecordedAt ? estimate : parked.RecordedAt;
    }

    /// <summary>
    /// Whether the odometer moved about as far as the gap between the parked spot and the first
    /// fix. Without both readings nothing confirms the gap was driven, so it is not.
    /// </summary>
    public static bool OdometerAgrees(double gapKm, double? beforeKm, double? afterKm)
    {
        if (beforeKm is not { } before || afterKm is not { } after) return false;
        var moved = after - before;
        return moved >= gapKm * MinOdometerRatio - OdometerResolutionKm &&
               moved <= gapKm * MaxOdometerRatio + OdometerResolutionKm;
    }

    /// <summary>
    /// The trip starting at the parked spot at <paramref name="departure"/>, or null when it should
    /// not: the spot is no candidate, the odometer disagrees, or leaving then would mean a speed no
    /// car drives.
    /// </summary>
    /// <param name="trip">The trip as cut from the fixes.</param>
    /// <param name="parked">The last fix the car reported standing still before it.</param>
    /// <param name="departure">When the car left, from <see cref="Departure"/>.</param>
    /// <param name="odometerAtDepartureKm">The odometer as last reported by <paramref name="departure"/>.</param>
    /// <param name="odometerAtFirstFixKm">The odometer as reported with the trip's first fix.</param>
    public static TripDto? Apply(
        TripDto trip, TripPoint parked, DateTime departure, double? odometerAtDepartureKm, double? odometerAtFirstFixKm)
    {
        var first = trip.Points[0];
        if (!IsCandidate(parked, first)) return null;

        var gapKm = GapKm(parked, first);
        if (!OdometerAgrees(gapKm, odometerAtDepartureKm, odometerAtFirstFixKm)) return null;

        var start = parked with { RecordedAt = departure, Source = TripPointSource.Anchored };
        if (!TripSegmenter.IsPlausibleHop(start, first)) return null;

        return trip with
        {
            StartedAt = departure,
            DistanceKm = Math.Round(trip.DistanceKm + gapKm, 2),
            PointCount = trip.PointCount + 1,
            Points = [start, .. trip.Points],
        };
    }
}
