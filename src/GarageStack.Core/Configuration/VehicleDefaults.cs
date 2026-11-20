namespace GarageStack.Core.Configuration;

/// <summary>
/// The deployment's defaults for every per-car setting, from its environment variables: what a car
/// gets for each value its <see cref="Models.VehicleSettings"/> leaves empty.
/// </summary>
/// <param name="Tyres">TYRE_PRESSURE_LOW_BAR, _GOOD_BAR, _HIGH_BAR and _DROP_BAR.</param>
/// <param name="Alerts">AUX_BATTERY_LOW_VOLT, LOW_FUEL_PERCENT and LOW_EV_PERCENT, with the stale-data hours that stay deployment-wide.</param>
/// <param name="Battery">HV_BATTERY_CAPACITY_KWH.</param>
public record VehicleDefaults(TyrePressureThresholds Tyres, AlertThresholds Alerts, HvBatteryCapacity Battery)
{
    public static readonly VehicleDefaults Default = new(
        TyrePressureThresholds.Default, AlertThresholds.Default, HvBatteryCapacity.Unknown);
}
