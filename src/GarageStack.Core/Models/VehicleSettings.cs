namespace GarageStack.Core.Models;

/// <summary>
/// What the user set for one car, in its own table beside the vehicle. Every value is optional:
/// left empty, the car takes the deployment's default (the environment variable named on each),
/// so an install that never opens these settings behaves as it did before they existed.
/// <see cref="Helpers.VehicleSettingsResolver"/> turns them into the values in effect. Anything
/// else kept per car, such as a default charge price, belongs here too.
/// </summary>
public class VehicleSettings
{
    /// <summary>Usable traction battery size in kWh (HV_BATTERY_CAPACITY_KWH). Not taken on a plain hybrid.</summary>
    public double? HvBatteryCapacityKwh { get; set; }

    /// <summary>Below this a tyre is low, in bar (TYRE_PRESSURE_LOW_BAR).</summary>
    public double? TyreLowBar { get; set; }

    /// <summary>From this a tyre is at its placard pressure, in bar (TYRE_PRESSURE_GOOD_BAR).</summary>
    public double? TyreGoodBar { get; set; }

    /// <summary>Above this a tyre is too high, in bar (TYRE_PRESSURE_HIGH_BAR).</summary>
    public double? TyreHighBar { get; set; }

    /// <summary>The slow-puncture alert's fall against the other tyres, in bar (TYRE_PRESSURE_DROP_BAR).</summary>
    public double? TyreDropBar { get; set; }

    /// <summary>12V battery voltage a parked car is warned below (AUX_BATTERY_LOW_VOLT).</summary>
    public double? AuxBatteryLowVolt { get; set; }

    /// <summary>Fuel level in percent the car is warned below (LOW_FUEL_PERCENT).</summary>
    public double? LowFuelPercent { get; set; }

    /// <summary>Charge level in percent a plug-in is warned below (LOW_EV_PERCENT).</summary>
    public double? LowEvPercent { get; set; }

    /// <summary>
    /// The drivetrain the user says the car has (hev, phev or bev), for when the one detected from
    /// what the gateway reports is wrong. Null follows the detected type.
    /// </summary>
    public string? VehicleType { get; set; }

    public VehicleSettings Copy() => (VehicleSettings)MemberwiseClone();
}
