namespace GarageStack.Core.Helpers;

/// <summary>
/// The power going into the car while it charges, in kW, from the three figures the gateway
/// publishes: obc/powerSinglePhase, obc/powerThreePhase and drivetrain/power, all already in kW.
///
/// The gateway works out the single-phase and the three-phase figure from the same onboard charger
/// voltage and current, and the car does not say which kind of connection it is on. Both cannot be
/// right: on a single-phase charger the three-phase figure reads about 58% of the truth, on a
/// three-phase one the single-phase figure reads 1.7 times it (issue #570: 2.5 against 4.3 kW while
/// the battery took 4.0). The battery's own power settles it, since what goes in at the plug only
/// differs from it by the charger's losses. A DC charge bypasses the onboard charger, which then
/// reads zero, so the battery's power is the figure there.
/// </summary>
public static class ChargerPower
{
    public static double? Kw(double? singlePhaseKw, double? threePhaseKw, double? hvPowerKw)
    {
        var single = Positive(singlePhaseKw);
        var three = Positive(threePhaseKw);
        var battery = hvPowerKw is { } hv && double.IsFinite(hv) && hv != 0 ? Math.Abs(hv) : (double?)null;

        if (single is { } s && three is { } t && battery is { } b)
            return Math.Abs(s - b) < Math.Abs(t - b) ? s : t;

        return three ?? single ?? battery;
    }

    private static double? Positive(double? value) =>
        value is { } v && double.IsFinite(v) && v > 0 ? v : null;
}
