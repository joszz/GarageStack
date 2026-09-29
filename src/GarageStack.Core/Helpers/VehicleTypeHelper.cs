using System.Text.RegularExpressions;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Works out a car's drivetrain from what the gateway reports about it: first the series code
/// (its Home Assistant hw_version), which names the drivetrain on some cars ("AS33HEV S",
/// "ZS EV L"), then the car's configuration codes for the many series codes that don't.
/// </summary>
public static partial class VehicleTypeHelper
{
    // A P straight after the model number marks the plug-in hybrids: AS33P S (HS Super Hybrid),
    // IS31P L (MG S9 PHEV). The BEV codes (EH32 S, AH4EM L, P12L, EP2CP3) never have one there.
    [GeneratedRegex(@"^[A-Z]+\d+P\b")]
    private static partial Regex PlugInHybridSeries();

    public static string GetVehicleType(Vehicle v)
    {
        var cfg = SafeJson.TryDeserialize<Dictionary<string, string>>(v.ConfigJson);
        var hw = (cfg?.GetValueOrDefault("hw_version") ?? "").ToUpperInvariant();
        // Order is load-bearing, most-specific first: "PHEV".Contains("HEV") and
        // "PHEV".Contains("EV") are both true, so checking HEV or EV before PHEV would
        // misclassify every plug-in hybrid. Do not reorder or alphabetize these checks.
        if (hw.Contains("PHEV")) return "phev";
        if (hw.Contains("HEV")) return "hev";
        if (hw.Contains("EV")) return "bev";

        // The BType code ("Battery") says whether the car plugs in: 0 on the plain hybrids (MG3
        // Hybrid+, HS Hybrid+), 1 or 2 on every BEV and PHEV. It cannot tell those two apart, and
        // neither can the EV and ENERGY codes: EV is 0 on MG4s and ENERGY is 1 on every drivetrain.
        var batteryType = BatteryType(cfg);
        if (batteryType is null) return "unknown";
        if (batteryType == "0") return "hev";
        return IsPlugInHybrid(hw, v.Model) ? "phev" : "bev";
    }

    /// <summary>Plug-in vehicles: the ones that charge from an external charger.</summary>
    public static bool CanCharge(string vehicleType) => vehicleType is "bev" or "phev";

    /// <summary>Vehicles that burn fuel and therefore need fuel stations on the map.</summary>
    public static bool HasCombustionEngine(string vehicleType) => vehicleType is "hev" or "phev";

    // Older cars send the code as BTYPE. "Battery" and "BATTERY" are different entries: the first is
    // BType under its item name, the second an unrelated code, so neither is read here.
    private static string? BatteryType(Dictionary<string, string>? cfg) =>
        cfg?.FirstOrDefault(kv => kv.Key.Equals("BType", StringComparison.OrdinalIgnoreCase)).Value?.Trim();

    // The gateway's model is the model name plus year and colour, e.g. "HS SUPER HYBRID 2025 ...".
    private static bool IsPlugInHybrid(string series, string? model)
    {
        var name = (model ?? "").ToUpperInvariant();
        return PlugInHybridSeries().IsMatch(series) || name.Contains("HYBRID") || name.Contains("PHEV");
    }
}
