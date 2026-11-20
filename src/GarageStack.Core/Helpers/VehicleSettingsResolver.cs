using GarageStack.Core.Configuration;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// The values in effect for one car: each of its own settings where it has one, the deployment's
/// default where it has not. The one place both the Api and the Worker take them from, so the
/// dashboard's colours and the alerts never disagree about a car.
/// </summary>
/// <param name="VehicleType">The drivetrain to treat the car as: the user's override, else the detected one.</param>
/// <param name="DetectedVehicleType">The drivetrain detected from what the gateway reports.</param>
/// <param name="HvBatteryCapacityKwh">The usable battery size in kWh, or null when nobody has said.</param>
/// <param name="Tyres">The tyre pressure bands and slow-puncture margin.</param>
/// <param name="Alerts">The levels the alerts fire at.</param>
public sealed record EffectiveVehicleSettings(
    string VehicleType,
    string DetectedVehicleType,
    double? HvBatteryCapacityKwh,
    TyrePressureThresholds Tyres,
    AlertThresholds Alerts);

public static class VehicleSettingsResolver
{
    public static EffectiveVehicleSettings Resolve(Vehicle vehicle, VehicleDefaults defaults)
    {
        var own = vehicle.Settings;
        var detected = VehicleTypeHelper.GetVehicleType(vehicle);
        var type = own?.VehicleType ?? detected;
        var tyres = defaults.Tyres;
        var alerts = defaults.Alerts;

        return new EffectiveVehicleSettings(
            type,
            detected,
            // A plain hybrid has no battery setting of its own (the settings page offers none),
            // so one left from when the car was taken for a plug-in does not count.
            type == "hev" ? defaults.Battery.Kwh : own?.HvBatteryCapacityKwh ?? defaults.Battery.Kwh,
            new TyrePressureThresholds(
                own?.TyreLowBar ?? tyres.LowBar,
                own?.TyreGoodBar ?? tyres.GoodBar,
                own?.TyreHighBar ?? tyres.HighBar,
                own?.TyreDropBar ?? tyres.DropBar),
            alerts with
            {
                AuxBatteryLowVolt = own?.AuxBatteryLowVolt ?? alerts.AuxBatteryLowVolt,
                LowFuelPercent = own?.LowFuelPercent ?? alerts.LowFuelPercent,
                LowEvPercent = own?.LowEvPercent ?? alerts.LowEvPercent,
            });
    }
}
