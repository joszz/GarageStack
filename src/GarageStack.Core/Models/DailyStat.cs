namespace GarageStack.Core.Models;

/// <summary>
/// One local day for the statistics page: the averages of what the car reported that day, and the
/// day's total off its energy counter. A value is null when the car reported none of it that day.
/// <c>CounterTotal</c> is in the counter's own unit: kWh out of the traction battery on a plug-in
/// car, hundredths of a litre of fuel on a plain hybrid (see the frontend's utils/energy).
/// <c>SinceChargeTotal</c> is the same day read off the since-last-charge counter instead, and
/// <c>CounterUnbacked</c> says the day counter rose more than that counter allows, which is how
/// a car whose day counter cannot be trusted shows itself (issue #551). The frontend decides
/// which of the two to show, because only it knows whether the car plugs in.
/// <c>DistanceKm</c> is the day's total off the car's distance-of-the-day counter, read the same
/// way as <c>CounterTotal</c>, so the two pair up into an efficiency for the same stretch of driving.
/// </summary>
public sealed record DailyStat(
    DateOnly Date,
    double? EvSocPercent,
    double? FuelLevelPercent,
    double? TyrePressureFrontLeft,
    double? TyrePressureFrontRight,
    double? TyrePressureRearLeft,
    double? TyrePressureRearRight,
    double? BatteryVoltage,
    double? CounterTotal,
    double? SinceChargeTotal = null,
    bool CounterUnbacked = false,
    double? DistanceKm = null);
