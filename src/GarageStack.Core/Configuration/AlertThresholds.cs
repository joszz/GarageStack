namespace GarageStack.Core.Configuration;

/// <summary>
/// When the Worker's level alerts fire. The defaults match the dashboard's own warning colours, so
/// a card turns red about when its alert goes out. Zero switches an alert off: no reading is below
/// zero, and no car is ever silent for zero hours.
/// </summary>
/// <param name="AuxBatteryLowVolt">The 12V battery voltage a parked car is warned below.</param>
/// <param name="LowFuelPercent">The fuel level a car that burns fuel is warned below.</param>
/// <param name="LowEvPercent">The charge level a plug-in is warned below.</param>
/// <param name="StaleDataHours">How long the car may go without reporting before that is an alert.</param>
public record AlertThresholds(double AuxBatteryLowVolt, double LowFuelPercent, double LowEvPercent, double StaleDataHours)
{
    // The gateway polls a sleeping car once a day by default, so two days of silence means at
    // least one of those polls failed rather than a quiet car.
    public static readonly AlertThresholds Default = new(
        AuxBatteryLowVolt: 12.0, LowFuelPercent: 15, LowEvPercent: 20, StaleDataHours: 48);
}
