using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

public static class VehicleTypeHelper
{
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
        return "unknown";
    }

    public static bool CanCharge(string vehicleType) => vehicleType is "bev" or "phev";
}
