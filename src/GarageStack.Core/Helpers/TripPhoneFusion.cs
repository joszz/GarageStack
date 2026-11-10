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
                var nearest = ordered[index];
                if (index + 1 < ordered.Length &&
                    Math.Abs((ordered[index + 1].RecordedAt - point.RecordedAt).TotalSeconds) <
                    Math.Abs((nearest.RecordedAt - point.RecordedAt).TotalSeconds))
                    nearest = ordered[index + 1];
                if (Math.Abs((nearest.RecordedAt - point.RecordedAt).TotalSeconds) > PairSeconds) continue;
                paired++;
                if (Distance(point, nearest) <= Math.Max(MatchMetres, 2 * (nearest.AccuracyM ?? 0)))
                {
                    matches++;
                    matchedTimes.Add(nearest.RecordedAt);
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

    private static bool Valid(PhoneFix f) =>
        !string.IsNullOrWhiteSpace(f.TrackerId) &&
        double.IsFinite(f.Latitude) && f.Latitude is >= -90 and <= 90 &&
        double.IsFinite(f.Longitude) && f.Longitude is >= -180 and <= 180 &&
        (f.AccuracyM is null || double.IsFinite(f.AccuracyM.Value) && f.AccuracyM is >= 0 and <= MaxAccuracyMetres);

    private static double Distance(TripPoint p, PhoneFix f) =>
        GeoHelper.Haversine(p.Latitude, p.Longitude, f.Latitude, f.Longitude) * 1000;
}
