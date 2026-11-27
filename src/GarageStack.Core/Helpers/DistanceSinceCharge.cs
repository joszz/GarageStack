namespace GarageStack.Core.Helpers;

/// <summary>
/// The distance driven since the last charge ended, worked out from the odometer for a car that does
/// not report its own distance-since-last-charge counter. Some cars send that counter (it starts
/// again from zero when a charge ends) and some never do; the odometer a charge ended at is in the
/// stored readings either way, and the distance is the odometer now less that one.
/// </summary>
public static class DistanceSinceCharge
{
    /// <summary>How far back the last charge is looked for: more than a year, so a car that is rarely plugged in still has one.</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(400);

    /// <summary>
    /// How long the odometer at the last charge is reused before it is looked up again. A charge
    /// ends rarely and the lookup reads far back, so a minute's delay in noticing one costs nothing.
    /// </summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    /// <summary>How many of the newest readings that show the car charging are compared, to find the latest charge.</summary>
    public const int ChargingReadingsCompared = 20;

    /// <summary>
    /// How far ahead of now a time may be and still be believed. A car's own clock is not ours, and
    /// an end time from the future would otherwise put the end of the last charge past every reading.
    /// </summary>
    private static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When a reading that shows the car charging was true of the car. A row's own time, unless the
    /// poll it carries is older than the row: a gateway that starts again sends its retained topics
    /// again, in a row of their own, which says "charging" about a charge that is long over (see
    /// <see cref="ChargeSessionHistory"/>).
    /// </summary>
    public static DateTime ChargingReadingAt(DateTime recordedAt, DateTime? chargeStateAt) =>
        chargeStateAt is { } stamp && stamp < recordedAt ? stamp : recordedAt;

    /// <summary>
    /// When the last charge ended, as far as the readings show: the later of the end time the car
    /// reported and the last time it was seen charging (a charge still going counts as ending now,
    /// so nothing has been driven since it). Null when neither is known.
    /// </summary>
    public static DateTime? LastChargeAt(DateTime now, DateTime? reportedEndAt, DateTime? lastSeenChargingAt)
    {
        var limit = now + ClockSlack;
        var reported = reportedEndAt is { } end && end <= limit ? end : (DateTime?)null;
        var seen = lastSeenChargingAt is { } at && at <= limit ? at : (DateTime?)null;
        return reported is { } r && seen is { } s ? (r > s ? r : s) : reported ?? seen;
    }

    /// <summary>
    /// How many points the state of charge must climb, with the car standing still, to be taken for
    /// a charge no reading caught. A parked car's figure does not climb on its own: in the stored
    /// readings it moves by tenths of a point at most, but a charge adds far more, so two points
    /// tells one from the other with room to spare and still finds a top-up worth the name.
    /// </summary>
    public const double MinimumSocRisePercent = 2;

    /// <summary>
    /// The state of charge at which a battery counts as full, and how many points a climb that ends
    /// there must have made: a parked car that reaches full has been charged, even when the last
    /// read before it was only a point or so below (98.3 to 100 is 1.7 points, short of
    /// <see cref="MinimumSocRisePercent"/>, and is the end of a charge all the same).
    /// </summary>
    public const double FullChargePercent = 99.5;

    /// <summary>The smallest climb to a full battery that is taken for a charge.</summary>
    public const double MinimumRiseToFullPercent = 1;

    /// <summary>How far back a rise in the state of charge is looked for when no charge is known to start from.</summary>
    public static readonly TimeSpan SocRiseLookback = TimeSpan.FromDays(60);

    /// <summary>A reading's state of charge and odometer, either of which a row may leave out.</summary>
    public sealed record SocReading(DateTime RecordedAt, double? OdometerKm, double? EvSocPercent);

    /// <summary>A charge found from the state of charge alone: when it was seen to have happened, and the odometer it happened at.</summary>
    public sealed record SocRiseCharge(DateTime At, double OdometerKm);

    /// <summary>
    /// The latest charge that shows only as the state of charge going up. The gateway reads a parked
    /// car rarely, so a charge can start and end between two reads, with nothing in them saying
    /// "charging". The car cannot have been driven while its odometer stood still, so a stretch of
    /// readings at one odometer value whose state of charge climbs by <paramref name="minimumRise"/>
    /// or more above its lowest in that stretch (a point is enough when it ends at a full battery) is a
    /// charge, and it happened at that odometer. A new
    /// odometer value starts a new stretch, so a drive (which regenerates and wobbles the figure) never counts.
    /// Null when there is none.
    /// </summary>
    public static SocRiseCharge? LastSocRiseCharge(
        IEnumerable<SocReading> readings, double minimumRise = MinimumSocRisePercent)
    {
        SocRiseCharge? found = null;
        double? odometer = null;
        double? stretchOdometer = null;
        double? lowest = null;

        foreach (var reading in readings.OrderBy(r => r.RecordedAt))
        {
            if (reading.OdometerKm is { } km && double.IsFinite(km))
                odometer = km;
            if (odometer != stretchOdometer)
            {
                stretchOdometer = odometer;
                lowest = null;
            }

            if (reading.EvSocPercent is not { } soc || !double.IsFinite(soc) || stretchOdometer is not { } at)
                continue;

            lowest = lowest is { } low && low < soc ? low : soc;
            var rise = soc - lowest.Value;
            if (rise >= minimumRise || (soc >= FullChargePercent && rise >= MinimumRiseToFullPercent))
            {
                found = new SocRiseCharge(reading.RecordedAt, at);
                // A charge goes on rising: the next two points count as the next sign of it, so the
                // time found follows the charge to its end.
                lowest = soc;
            }
        }

        return found;
    }

    /// <summary>
    /// The kilometres between the odometer at the end of the last charge and the odometer now, to a
    /// tenth. Null when either is unknown, or when the odometer went backwards: a reading that says
    /// so is wrong somewhere, and nothing is better than a negative distance.
    /// </summary>
    public static double? Between(double? odometerKm, double? odometerAtChargeKm)
    {
        if (odometerKm is not { } now || odometerAtChargeKm is not { } then) return null;
        if (!double.IsFinite(now) || !double.IsFinite(then) || now < then) return null;
        return Math.Round(now - then, 1);
    }
}
