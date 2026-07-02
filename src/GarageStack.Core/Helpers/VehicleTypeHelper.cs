using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

public static class VehicleTypeHelper
{
    public static string GetVehicleType(Vehicle v)
    {
        var cfg = SafeJson.TryDeserialize<Dictionary<string, string>>(v.ConfigJson);
        var hw = (cfg?.GetValueOrDefault("hw_version") ?? "").ToUpperInvariant();
        if (hw.Contains("PHEV")) return "phev";
        if (hw.Contains("HEV")) return "hev";
        if (hw.Contains("EV")) return "bev";
        return "unknown";
    }

    public static bool CanCharge(string vehicleType) => vehicleType is "bev" or "phev";
}
