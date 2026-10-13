namespace GarageStack.Core.Models;

/// <summary>
/// One local day for the statistics page: the averages of what the car reported that day, and the
/// day's total off its energy counter. A value is null when the car reported none of it that day.
/// <c>CounterTotal</c> is in the counter's own unit: kWh out of the traction battery on a plug-in
/// car, hundredths of a litre of fuel on a plain hybrid (see the frontend's utils/energy).
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
    double? CounterTotal);
