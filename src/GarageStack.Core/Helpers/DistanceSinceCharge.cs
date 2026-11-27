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
