namespace GarageStack.Core.Helpers;

/// <summary>
/// The car's own "distance today" counter, checked against the odometer. Some cars fill that
/// counter with the distance since delivery instead (a 2026 MG HS plug-in hybrid, issue #573), so
/// the dashboard showed the odometer as today's distance. No car drives further than its odometer
/// moves, so a counter beyond the odometer's gain over the last two days is not a day counter, and
/// the odometer's gain since the start of the day stands in for it.
/// </summary>
public static class DayDistance
{
    /// <summary>
    /// How far back the check looks. Two days, not one: until the car resets the counter after
    /// midnight it can still hold yesterday's total, which is a real figure, not a lifetime one.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(2);

    // Room for a counter and an odometer reported a few tenths apart, or from different polls.
    private const double ToleranceKm = 2;

    /// <summary>Whether the counter reads more than the car can have driven in <see cref="Window"/>.</summary>
    public static bool IsBeyondOdometer(double counterKm, double odometerKm, double odometerWindowStartKm) =>
        counterKm > odometerKm - odometerWindowStartKm + ToleranceKm;

    /// <summary>
    /// The odometer's gain since the start of the day, standing in for a counter that failed the
    /// check. Null without a reading to start from, so the card hides rather than guesses.
    /// </summary>
    public static double? FromOdometer(double odometerKm, double? odometerDayStartKm) =>
        odometerDayStartKm is { } start ? Math.Round(Math.Max(0, odometerKm - start), 1) : null;
}
