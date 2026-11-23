using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// What a car is fitted with, read from its configuration codes (the gateway's
/// info/configuration/* topics) by the rules saic-python-mqtt-gateway 0.14.0-rc5 uses
/// (src/vehicle_info.py). A code that is missing or unreadable keeps the equipment: some cars
/// leave codes out and still send the data.
/// </summary>
public static class VehicleEquipment
{
    /// <summary>The door positions, in the order of the DOOR code's mask.</summary>
    public enum Door
    {
        Driver = 0,
        Passenger = 1,
        RearLeft = 2,
        RearRight = 3,
    }

    // The gateway's series code (vin_info.series), stored under its Home Assistant name.
    private const string SeriesKey = "hw_version";

    // Codes are matched exactly. The gateway also publishes every item under its name, and a name
    // can match another code apart from case ("Battery" is BType, "BATTERY" a different code).
    private const string DoorCode = "DOOR";
    private const string SunroofCode = "S35";
    private const string PowerTailgateCode = "CTRLBOOT";

    // The only series with a power tailgate when CTRLBOOT does not say: the MG S6 EV.
    private const string PowerTailgateSeries = "MIS3E";

    /// <summary>
    /// Whether the car has the door. DOOR is a four-character mask (driver, passenger, rear left,
    /// rear right) of 1s and 0s: the two-door Cyberster reports 1100.
    /// </summary>
    public static bool HasDoor(Vehicle v, Door door) => HasDoor(Config(v), door);

    /// <summary>
    /// Whether the car has a sunroof. S35 is 0 without one and 3 on cars with one; a 2 on its own
    /// does not mean a sunroof either.
    /// </summary>
    public static bool HasSunroof(Vehicle v) => HasSunroof(Config(v));

    /// <summary>
    /// Whether the car's codes say it has a power tailgate, as the iSmart app reads them: CTRLBOOT 2
    /// adds one and 0 removes it. Otherwise the series decides, and only the MG S6 EV (MIS3E) has
    /// one, not its long variant: the app drops " S" from the series and takes any "L" left as long.
    /// </summary>
    public static bool ReportsPowerTailgate(Vehicle v)
    {
        var config = Config(v);
        switch (config?.GetValueOrDefault(PowerTailgateCode)?.Trim())
        {
            case "2": return true;
            case "0": return false;
        }

        var series = (config?.GetValueOrDefault(SeriesKey) ?? "").Replace(" S", "", StringComparison.Ordinal);
        return series.Contains(PowerTailgateSeries, StringComparison.Ordinal) && !series.Contains('L', StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the boot opens and closes remotely: what the user set, else what the car reports.
    /// See <see cref="Vehicle.HasPowerTailgate"/>.
    /// </summary>
    public static bool HasPowerTailgate(Vehicle v) => v.HasPowerTailgate ?? ReportsPowerTailgate(v);

    /// <summary>
    /// The snapshot with no reading for equipment the car does not have: a missing door and the
    /// window in it, and a missing sunroof, so nothing shows them. Some cars report a fixed value
    /// for these (a sunroof always "open"). The snapshot itself is left alone, since it may be the
    /// cached one: a copy is returned when anything changes.
    /// </summary>
    public static TelemetrySnapshot WithoutMissingEquipment(TelemetrySnapshot snapshot, Vehicle v)
    {
        var config = Config(v);
        var hasSunroof = HasSunroof(config);
        var missingDoors = Enum.GetValues<Door>().Where(d => !HasDoor(config, d)).ToList();
        if (hasSunroof && missingDoors.Count == 0) return snapshot;

        var copy = snapshot.Clone();
        if (!hasSunroof) copy.SunRoofOpen = null;
        foreach (var door in missingDoors)
        {
            switch (door)
            {
                case Door.Driver:
                    copy.DriverDoorOpen = null;
                    copy.DriverWindowOpen = null;
                    break;
                case Door.Passenger:
                    copy.PassengerDoorOpen = null;
                    copy.PassengerWindowOpen = null;
                    break;
                case Door.RearLeft:
                    copy.RearLeftDoorOpen = null;
                    copy.RearLeftWindowOpen = null;
                    break;
                case Door.RearRight:
                    copy.RearRightDoorOpen = null;
                    copy.RearRightWindowOpen = null;
                    break;
            }
        }
        return copy;
    }

    private static Dictionary<string, string>? Config(Vehicle v) =>
        SafeJson.TryDeserialize<Dictionary<string, string>>(v.ConfigJson);

    private static bool HasDoor(Dictionary<string, string>? config, Door door)
    {
        var mask = config?.GetValueOrDefault(DoorCode)?.Trim() ?? "";
        if (mask.Length != 4 || mask.Any(c => c is not ('0' or '1'))) return true;
        return mask[(int)door] == '1';
    }

    private static bool HasSunroof(Dictionary<string, string>? config) =>
        !int.TryParse(config?.GetValueOrDefault(SunroofCode)?.Trim(), out var value) || (value & ~2) != 0;
}
