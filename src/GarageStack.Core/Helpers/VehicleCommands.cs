using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// The commands GarageStack sends, each with the gateway topic it travels on and the values the
/// gateway takes for it. A command is published on {topic}/set and the gateway answers on
/// {topic}/result, so one table serves both directions: sending a command, and naming the command
/// an answer belongs to. Adding a command means adding one entry here. It lives in Core because
/// both processes need it: the Api sends commands, and the Worker's climate schedules wait for the
/// answer on the command's topic.
/// </summary>
public static class VehicleCommands
{
    /// <param name="Topic">The topic as saic-python-mqtt-gateway's handlers register it (src/mqtt_topics.py).</param>
    /// <param name="Check">Why a value is refused, or null when the gateway takes it.</param>
    /// <param name="Supports">
    /// Whether the gateway carries the command, with this value, out on this car, for a command it
    /// otherwise drops while still answering "Success"; null when the car itself says no.
    /// </param>
    private sealed record Command(string Topic, Func<string, string?> Check, Func<Vehicle, string, bool>? Supports = null);

    // The gateway maps this onto its ChargeCurrentLimitCode enum, which only knows these four
    // values (it upper-cases the payload first, so any casing is accepted here).
    private static readonly HashSet<string> ChargeCurrentLimits =
        new(["6A", "8A", "16A", "MAX"], StringComparer.OrdinalIgnoreCase);

    // The gateway maps the percentage onto TargetBatteryCode, which knows 40 to 100 in steps of 10.
    private static readonly HashSet<string> ChargeTargets = ["40", "50", "60", "70", "80", "90", "100"];

    // The names of saic-ismart-client-ng's ScheduledChargingMode, as the gateway reports them.
    private const string UntilTargetMode = "UNTIL_CONFIGURED_SOC";
    private static readonly HashSet<string> ChargingScheduleModes = ["UNTIL_CONFIGURED_TIME", UntilTargetMode, "DISABLED"];

    // A schedule is a small JSON object; anything longer is not one, and is not parsed.
    private const int MaxScheduleLength = 500;

    private static readonly Dictionary<string, Command> ByName = new(StringComparer.Ordinal)
    {
        // Besides on and off the gateway takes "front" (front window defrost) and "blowingonly"
        // (fan only), each a remote climate mode of its own.
        ["climate"] = new("climate/remoteClimateState", value =>
            value is "on" or "off" or "front" or "blowingonly"
                ? null
                : "'climate' value must be 'on', 'off', 'front' or 'blowingonly'"),
        ["climate-temperature"] = new("climate/remoteTemperature", value => IntegerBetween("climate-temperature", value, 16, 28)),
        ["rear-defroster"] = new("climate/rearWindowDefrosterHeating", value => OnOff("rear-defroster", value)),
        ["seat-left"] = new("climate/heatedSeatsFrontLeftLevel", value => IntegerBetween("seat-left", value, 0, 3)),
        ["seat-right"] = new("climate/heatedSeatsFrontRightLevel", value => IntegerBetween("seat-right", value, 0, 3)),
        ["find-my-car"] = new("location/findMyCar", value =>
            value is "activate" or "stop" ? null : "'find-my-car' value must be 'activate' or 'stop'"),
        ["charge-limit"] = new("drivetrain/chargeCurrentLimit", value =>
            ChargeCurrentLimits.Contains(value)
                ? null
                : $"'charge-limit' value must be one of {string.Join(", ", ChargeCurrentLimits)}"),
        // The gateway drops a target on a car it does not take one for, and answers "Success" all
        // the same, so such a car is refused here instead.
        ["charge-target"] = new("drivetrain/socTarget", value =>
            ChargeTargets.Contains(value)
                ? null
                : $"'charge-target' value must be one of {string.Join(", ", ChargeTargets)}",
            (vehicle, _) => VehicleTypeHelper.SupportsTargetSoc(vehicle)),
        ["charging"] = new("drivetrain/charging", value => TrueFalse("charging", value)),
        ["charging-cable-lock"] = new("drivetrain/chargingCableLock", value => TrueFalse("charging-cable-lock", value)),
        ["battery-heating"] = new("drivetrain/batteryHeating", value => TrueFalse("battery-heating", value)),
        // The gateway reads both times whatever the mode, so even switching the schedule off needs
        // them. Charging until the target is dropped like a target, on a car that takes none.
        ["scheduled-charging"] = new("drivetrain/chargingSchedule", ChargingSchedule,
            (vehicle, value) => ScheduleField(value, "mode") != UntilTargetMode || VehicleTypeHelper.SupportsTargetSoc(vehicle)),
        // The gateway reads the start time even when the schedule is switched off.
        ["battery-heating-schedule"] = new("drivetrain/batteryHeatingSchedule", BatteryHeatingSchedule),
        ["lock"] = new("doors/locked", value =>
            value is "True" or "False" ? null : "'lock' value must be 'True' or 'False'"),
        // "false" unlocks the tailgate. The gateway takes "true" too, but only logs that a boot
        // cannot be closed remotely and answers "Success", so it is not offered.
        ["boot"] = new("doors/boot", value =>
            value == "false" ? null : "'boot' value must be 'false': the boot can only be opened"),
        ["refresh"] = new("refresh/mode", value =>
            value == "force" ? null : "'refresh' value must be 'force'"),
    };

    private static readonly Dictionary<string, string> CommandsByTopic =
        ByName.ToDictionary(pair => pair.Value.Topic, pair => pair.Key, StringComparer.Ordinal);

    private static string? OnOff(string command, string value) =>
        value is "on" or "off" ? null : $"'{command}' value must be 'on' or 'off'";

    private static string? TrueFalse(string command, string value) =>
        value is "true" or "false" ? null : $"'{command}' value must be 'true' or 'false'";

    // The schedules travel as JSON objects of strings, which the gateway reads with an enum lookup
    // and time.fromisoformat; it answers anything else with "Failed: Error converting payload".
    private static Dictionary<string, string>? ScheduleFields(string value) =>
        value.Length <= MaxScheduleLength ? SafeJson.TryDeserialize<Dictionary<string, string>>(value) : null;

    private static string? ScheduleField(string value, string name) => ScheduleFields(value)?.GetValueOrDefault(name);

    private static bool IsTimeOfDay(string? value) => TimeOfDayText.TryParse(value, out _);

    private static string? ChargingSchedule(string value) =>
        ScheduleFields(value) is { } fields
        && ChargingScheduleModes.Contains(fields.GetValueOrDefault("mode") ?? "")
        && IsTimeOfDay(fields.GetValueOrDefault("startTime"))
        && IsTimeOfDay(fields.GetValueOrDefault("endTime"))
            ? null
            : $"'scheduled-charging' value must be a JSON object with mode ({string.Join(", ", ChargingScheduleModes)}), startTime and endTime (HH:mm)";

    private static string? BatteryHeatingSchedule(string value) =>
        ScheduleFields(value) is { } fields
        && fields.GetValueOrDefault("mode") is "on" or "off"
        && IsTimeOfDay(fields.GetValueOrDefault("startTime"))
            ? null
            : "'battery-heating-schedule' value must be a JSON object with mode ('on' or 'off') and startTime (HH:mm)";

    private static string? IntegerBetween(string command, string value, int min, int max) =>
        int.TryParse(value, out var number) && number >= min && number <= max
            ? null
            : $"'{command}' value must be an integer between {min} and {max}";

    /// <summary>The gateway topic for <paramref name="command"/>, or null for a command GarageStack does not know.</summary>
    public static string? TopicFor(string command) => ByName.GetValueOrDefault(command)?.Topic;

    /// <summary>
    /// The command sent on <paramref name="topic"/>, or null when it is not one GarageStack sends,
    /// such as a command Home Assistant sent through the same broker.
    /// </summary>
    public static string? CommandFor(string topic) => CommandsByTopic.GetValueOrDefault(topic);

    /// <summary>
    /// Why the gateway would refuse <paramref name="value"/> for <paramref name="command"/> (an
    /// English message), or null when it takes it. A command GarageStack does not know is refused
    /// by <see cref="TopicFor"/>.
    /// </summary>
    public static string? Validate(string command, string value) =>
        ByName.GetValueOrDefault(command)?.Check(value);

    /// <summary>
    /// Whether the gateway carries <paramref name="command"/> with <paramref name="value"/> out on
    /// <paramref name="vehicle"/>. True for a command the car itself accepts or refuses, which
    /// answers for itself.
    /// </summary>
    public static bool Supports(string command, Vehicle vehicle, string value) =>
        ByName.GetValueOrDefault(command)?.Supports?.Invoke(vehicle, value) ?? true;
}
