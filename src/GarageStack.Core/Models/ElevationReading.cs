namespace GarageStack.Core.Models;

/// <summary>The height above sea level the car's GPS reported at one moment, in metres.</summary>
public sealed record ElevationReading(DateTime RecordedAt, double ElevationM)
{
    // The Dead Sea shore at about -430 m and the highest pass a car reaches at about 5,800 m. A
    // GPS without a fix reports 0 or a wild value, and only the latter can be told apart.
    public const double LowestRoadM = -450;
    public const double HighestRoadM = 6_000;

    /// <summary>The longest profile served: a long day's drive at a fix a minute, with room to spare.</summary>
    public const int MaxReadings = 2_000;

    public static bool IsPlausible(double elevationM) =>
        double.IsFinite(elevationM) && elevationM is >= LowestRoadM and <= HighestRoadM;
}
