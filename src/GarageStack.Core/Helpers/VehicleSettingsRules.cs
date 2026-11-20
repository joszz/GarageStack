using System.Globalization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// What a car's settings may hold. A value outside its range is refused rather than clamped, so
/// nobody is left thinking a setting took that did not.
/// </summary>
public static class VehicleSettingsRules
{
    /// <summary>The types a car can be set to; "unknown" is only ever detected.</summary>
    public static readonly IReadOnlyList<string> VehicleTypes = ["hev", "phev", "bev"];

    public const string OutOfRangeCode = "vehicleSettings.outOfRange";
    public const string TyreOrderCode = "vehicleSettings.tyreOrder";
    public const string BatteryOnHybridCode = "vehicleSettings.batteryOnHybrid";
    public const string VehicleTypeUnknownCode = "vehicleSettings.vehicleTypeUnknown";

    /// <summary>A number setting and the range it may take, inclusive.</summary>
    public sealed record NumberRule(
        string Name, double Min, double Max, Func<VehicleSettings, double?> Get, Action<VehicleSettings, double?> Set);

    // Zero is in range wherever the deployment's own variable takes it to switch an alert off.
    public static readonly IReadOnlyList<NumberRule> Numbers =
    [
        new("hvBatteryCapacityKwh", 0.5, 200, s => s.HvBatteryCapacityKwh, (s, v) => s.HvBatteryCapacityKwh = v),
        new("tyreLowBar", 1, 5, s => s.TyreLowBar, (s, v) => s.TyreLowBar = v),
        new("tyreGoodBar", 1, 5, s => s.TyreGoodBar, (s, v) => s.TyreGoodBar = v),
        new("tyreHighBar", 1, 5, s => s.TyreHighBar, (s, v) => s.TyreHighBar = v),
        new("tyreDropBar", 0, 1, s => s.TyreDropBar, (s, v) => s.TyreDropBar = v),
        new("auxBatteryLowVolt", 0, 15, s => s.AuxBatteryLowVolt, (s, v) => s.AuxBatteryLowVolt = v),
        new("lowFuelPercent", 0, 100, s => s.LowFuelPercent, (s, v) => s.LowFuelPercent = v),
        new("lowEvPercent", 0, 100, s => s.LowEvPercent, (s, v) => s.LowEvPercent = v),
    ];

    /// <summary>
    /// Why <paramref name="settings"/> cannot be saved for <paramref name="vehicle"/>, as a code and
    /// an English message, or null when they can. The tyre bands are checked as they would take
    /// effect, so a low pressure above the deployment's good one is refused too.
    /// </summary>
    public static (string Code, string Message)? Check(Vehicle vehicle, VehicleSettings settings, VehicleDefaults defaults)
    {
        foreach (var rule in Numbers)
        {
            if (rule.Get(settings) is { } value && (!double.IsFinite(value) || value < rule.Min || value > rule.Max))
                return (OutOfRangeCode, string.Create(CultureInfo.InvariantCulture,
                    $"{rule.Name} must be from {rule.Min} to {rule.Max}"));
        }

        if (settings.VehicleType is { } type && !VehicleTypes.Contains(type))
            return (VehicleTypeUnknownCode, "vehicleType must be hev, phev or bev");

        var effective = VehicleSettingsResolver.Resolve(new Vehicle { ConfigJson = vehicle.ConfigJson, Model = vehicle.Model, Settings = settings }, defaults);
        if (effective.Tyres.LowBar > effective.Tyres.GoodBar || effective.Tyres.GoodBar > effective.Tyres.HighBar)
            return (TyreOrderCode, string.Create(CultureInfo.InvariantCulture,
                $"The tyre pressures must run low, good, high: {effective.Tyres.LowBar}, {effective.Tyres.GoodBar} and {effective.Tyres.HighBar} do not"));

        if (settings.HvBatteryCapacityKwh is not null && effective.VehicleType == "hev")
            return (BatteryOnHybridCode, "A plain hybrid takes no battery capacity of its own: clear hvBatteryCapacityKwh, or set another vehicleType");

        return null;
    }
}
