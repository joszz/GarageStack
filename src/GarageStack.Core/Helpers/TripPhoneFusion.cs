using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

public static class TripPhoneFusion
{
    /// <summary>Phone fixes with worse horizontal accuracy are ignored.</summary>
    public const double MaxAccuracyMetres = 50;
    /// <summary>Maximum time difference when confirming that phone and car travel together.</summary>
    public const int PairSeconds = 60;
    /// <summary>The minimum spatial tolerance for a confirming pair.</summary>
    public const double MatchMetres = 300;
    /// <summary>At least this share of paired car fixes must agree with the phone.</summary>
    public const double MinimumMatchRatio = 0.7;
    /// <summary>One coincidental pair cannot establish that the phone is in the car.</summary>
    public const int MinimumMatches = 2;
    /// <summary>Car fixes take precedence over phone fixes this close in time.</summary>
    public const int CarPrioritySeconds = 20;
    /// <summary>A recovered start must leave the car's known parking spot.</summary>
    public const double ParkedMetres = 200;

    public static PhoneFusionResult Apply(
        IReadOnlyList<TripPoint> points, TripPoint? parked, DateTime? previousEnd,
        IReadOnlyList<PhoneFix> fixes)
    {
        var car = points.Where(p => p.Source is null).OrderBy(p => p.RecordedAt).ToArray();
        if (car.Length < MinimumMatches) return new(points, PhoneFusionOutcome.NoMatch);

        PhoneFix[]? best = null;
        double bestRatio = 0;
        var bestMatches = 0;
        foreach (var tracker in fixes.Where(Valid).GroupBy(f => f.TrackerId).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ordered = tracker.OrderBy(p => p.RecordedAt).DistinctBy(p => p.RecordedAt).ToArray();
            var paired = 0;
            var matches = 0;
            var index = 0;
            var matchedTimes = new HashSet<DateTime>();
            foreach (var point in car)
            {
                while (index + 1 < ordered.Length && ordered[index + 1].RecordedAt <= point.RecordedAt) index++;
                if (PhoneAt(ordered, index, point.RecordedAt) is not { } phone) continue;
                paired++;
                if (GeoHelper.Haversine(point.Latitude, point.Longitude, phone.Latitude, phone.Longitude) * 1000 <= phone.Tolerance)
                {
                    matches++;
                    matchedTimes.Add(phone.Fix);
                }
            }
            var ratio = paired == 0 ? 0 : (double)matches / paired;
            if (matches < MinimumMatches || matchedTimes.Count < MinimumMatches || ratio < MinimumMatchRatio) continue;
            if (best is null || ratio > bestRatio || ratio == bestRatio && matches > bestMatches)
                (best, bestRatio, bestMatches) = (ordered, ratio, matches);
        }
        if (best is null) return new(points, PhoneFusionOutcome.NoMatch);

        var from = points[0].RecordedAt;
        // An existing anchor stays the first point. Otherwise a confirmed parked start can extend it.
        var earliest = parked?.RecordedAt ?? from;
        if (points[0].Source == TripPointSource.Anchored && earliest < from) earliest = from;
        if (previousEnd is { } end && earliest < end) earliest = end;
        var early = best.FirstOrDefault(p => p.RecordedAt >= earliest && p.RecordedAt < car[0].RecordedAt);
        var recoverStart = parked is not null && early is not null && Distance(parked, early) <= ParkedMetres;
        if (recoverStart) from = earliest;
        else from = car[0].RecordedAt;

        var existingTimes = points.Select(p => p.RecordedAt).ToHashSet();
        var additions = new List<TripPoint>();
        var carIndex = 0;
        foreach (var fix in best)
        {
            if (fix.RecordedAt < from || fix.RecordedAt >= points[^1].RecordedAt || existingTimes.Contains(fix.RecordedAt)) continue;
            while (carIndex + 1 < car.Length && car[carIndex + 1].RecordedAt <= fix.RecordedAt) carIndex++;
            if (Math.Abs((car[carIndex].RecordedAt - fix.RecordedAt).TotalSeconds) <= CarPrioritySeconds ||
                carIndex + 1 < car.Length && Math.Abs((car[carIndex + 1].RecordedAt - fix.RecordedAt).TotalSeconds) <= CarPrioritySeconds)
                continue;
            additions.Add(new(fix.RecordedAt, fix.Latitude, fix.Longitude, fix.SpeedKmh, TripPointSource.Phone));
        }
        return additions.Count == 0
            ? new(points, PhoneFusionOutcome.NoMatch)
            : new(points.Concat(additions).OrderBy(p => p.RecordedAt).ToArray(), PhoneFusionOutcome.Fused);
    }

    /// <summary>
    /// Where the phone was when the car reported at <paramref name="at"/>, how far from it the car
    /// may be, and the phone fix that answers for it; null when no fix is close enough in time.
    /// Between two fixes at most twice <see cref="PairSeconds"/> apart the position is interpolated,
    /// so a fast car is not failed only because the phone sampled at other moments: at 120 km/h,
    /// ten seconds apart is already 330 metres. Elsewhere it is the nearest fix within
    /// <see cref="PairSeconds"/>. <paramref name="index"/> is the last fix not after <paramref name="at"/>,
    /// or the first fix when all of them are later.
    /// </summary>
    private static (double Latitude, double Longitude, double Tolerance, DateTime Fix)? PhoneAt(
        PhoneFix[] ordered, int index, DateTime at)
    {
        var before = ordered[index];
        var after = index + 1 < ordered.Length ? ordered[index + 1] : null;
        if (before.RecordedAt <= at && after is not null &&
            (after.RecordedAt - before.RecordedAt).TotalSeconds <= 2 * PairSeconds)
        {
            var share = (at - before.RecordedAt) / (after.RecordedAt - before.RecordedAt);
            var nearer = share <= 0.5 ? before : after;
            return (before.Latitude + share * (after.Latitude - before.Latitude),
                before.Longitude + share * (after.Longitude - before.Longitude),
                Math.Max(MatchMetres, 2 * Math.Max(before.AccuracyM ?? 0, after.AccuracyM ?? 0)),
                nearer.RecordedAt);
        }

        var nearest = after is not null &&
            Math.Abs((after.RecordedAt - at).TotalSeconds) < Math.Abs((before.RecordedAt - at).TotalSeconds)
            ? after : before;
        if (Math.Abs((nearest.RecordedAt - at).TotalSeconds) > PairSeconds) return null;
        return (nearest.Latitude, nearest.Longitude, Math.Max(MatchMetres, 2 * (nearest.AccuracyM ?? 0)), nearest.RecordedAt);
    }

    private static bool Valid(PhoneFix f) =>
        !string.IsNullOrWhiteSpace(f.TrackerId) &&
        double.IsFinite(f.Latitude) && f.Latitude is >= -90 and <= 90 &&
        double.IsFinite(f.Longitude) && f.Longitude is >= -180 and <= 180 &&
        (f.AccuracyM is null || double.IsFinite(f.AccuracyM.Value) && f.AccuracyM is >= 0 and <= MaxAccuracyMetres);

    private static double Distance(TripPoint p, PhoneFix f) =>
        GeoHelper.Haversine(p.Latitude, p.Longitude, f.Latitude, f.Longitude) * 1000;
}
