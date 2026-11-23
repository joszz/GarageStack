namespace GarageStack.Core.Helpers;

/// <summary>
/// What the car's day counter (powerUsageOfDay) rose by over one trip: the fuel a hybrid burned, in
/// its counter's hundredths of a litre, or the energy a plug-in car took, in its counter's kWh. The
/// frontend decides which unit applies, as it does for the day totals (utils/energy.ts).
/// </summary>
public static class TripCounterUse
{
    // A reading below this is taken for a counter reset rather than noise, as DailyStatsBuilder does.
    private const double ResetNoiseFloor = 0.05;

    // A reading below this fraction of the running peak is taken for a counter reset.
    private const double ResetPeakFraction = 0.05;

    /// <summary>
    /// The counter's rise over <paramref name="readings"/>, oldest first, the first being the last
    /// reading before the trip. The counter starts again from zero once a day, sometimes during a
    /// trip that runs past midnight, so a reading that falls to next to nothing counts in full as
    /// new use. A smaller dip is a reading out of step, which counts neither way: the rise starts
    /// again only above the highest reading before it. Null with fewer than two readings, since a
    /// single one says nothing about the trip.
    /// </summary>
    public static double? Rise(IReadOnlyList<double> readings)
    {
        if (readings.Count < 2) return null;

        var total = 0.0;
        var peak = readings[0];
        for (var i = 1; i < readings.Count; i++)
        {
            var reading = readings[i];
            if (reading < Math.Max(peak * ResetPeakFraction, ResetNoiseFloor) && peak > ResetNoiseFloor)
            {
                total += reading;
                peak = reading;
            }
            else if (reading > peak)
            {
                total += reading - peak;
                peak = reading;
            }
        }

        return Math.Round(total, 2);
    }
}
