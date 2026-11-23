using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>What the car did over a week or a month, for its summary notification.</summary>
/// <param name="Trips">The trips saved in the period, hidden ones left out.</param>
/// <param name="DistanceKm">The car's own day counter over the period, or the trips' when it has none.</param>
/// <param name="FuelLitres">What a plain hybrid burned; null for any other drivetrain.</param>
/// <param name="EnergyKwh">What a BEV took from its battery; null for any other drivetrain.</param>
/// <param name="PerHundredKm">Fuel or energy per 100 km over the days that counted both; null under 5 km.</param>
public sealed record DrivingSummaryFigures(
    int Trips, double DistanceKm, double? FuelLitres, double? EnergyKwh, double? PerHundredKm)
{
    public bool HasDriving => Trips > 0 || DistanceKm > 0;
}

/// <summary>
/// Adds a period up the way the statistics page does (frontend utils/energy.ts and
/// utils/statistics.ts): the counted distance before the trips' GPS distance, a hybrid's day
/// counter as hundredths of a litre, a BEV's as kWh unless the day counter ran past the
/// since-last-charge one (issue #551). A plug-in hybrid's kWh come on a nominal pack the Worker
/// cannot scale, so its summary leaves the energy out rather than guess.
/// </summary>
public static class DrivingSummary
{
    private const double HundredthsPerLitre = 100;

    // As MIN_EFFICIENCY_DISTANCE_KM: below this an efficiency is mostly rounding.
    private const double MinEfficiencyKm = 5;

    public static DrivingSummaryFigures Build(
        IReadOnlyList<TripSummaryDto> trips, IReadOnlyList<DailyStat> days, string vehicleType)
    {
        var counted = days.Where(d => d.DistanceKm is not null).Sum(d => d.DistanceKm!.Value);
        var distance = counted > 0 ? counted : trips.Sum(t => t.DistanceKm);

        Func<DailyStat, double?>? used = vehicleType switch
        {
            "hev" => d => d.CounterTotal / HundredthsPerLitre,
            "bev" => d => d.CounterUnbacked ? d.SinceChargeTotal : d.CounterTotal,
            _ => null,
        };
        if (used is null) return new(trips.Count, distance, null, null, null);

        var total = days.Sum(d => used(d) ?? 0);
        var paired = days.Where(d => used(d) is not null && d.DistanceKm is not null).ToList();
        var pairedKm = paired.Sum(d => d.DistanceKm!.Value);
        double? perHundred = pairedKm >= MinEfficiencyKm ? paired.Sum(d => used(d)!.Value) / pairedKm * 100 : null;

        return vehicleType == "hev"
            ? new(trips.Count, distance, total, null, perHundred)
            : new(trips.Count, distance, null, total, perHundred);
    }
}
