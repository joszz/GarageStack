using System.Text.Json;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

public static class VehicleTypeHelper
{
    public static string GetVehicleType(Vehicle v)
    {
        if (v.ConfigJson is null) return "unknown";
        try
        {
            var cfg = JsonSerializer.Deserialize<Dictionary<string, string>>(v.ConfigJson);
            var hw = (cfg?.GetValueOrDefault("hw_version") ?? "").ToUpperInvariant();
            if (hw.Contains("PHEV")) return "phev";
            if (hw.Contains("HEV")) return "hev";
            if (hw.Contains("BEV") || hw.Contains("EV")) return "bev";
        }
        catch { }
        return "unknown";
    }

    public static bool CanCharge(string vehicleType) => vehicleType is "bev" or "phev";
}
