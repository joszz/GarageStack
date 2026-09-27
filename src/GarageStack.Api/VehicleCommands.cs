using GarageStack.Api.Endpoints;

namespace GarageStack.Api;

/// <summary>
/// The commands the API accepts, each with the gateway topic it travels on and the values the
/// gateway takes for it. A command is published on {topic}/set and the gateway answers on
/// {topic}/result, so one table serves both directions: sending a command, and naming the command
/// an answer belongs to. Adding a command means adding one entry here.
/// </summary>
internal static class VehicleCommands
{
    /// <param name="Topic">The topic as saic-python-mqtt-gateway's handlers register it (src/mqtt_topics.py).</param>
    /// <param name="Check">Why a value is refused, or null when the gateway takes it.</param>
    private sealed record Command(string Topic, Func<string, string?> Check);

    // The gateway maps this onto its ChargeCurrentLimitCode enum, which only knows these four
    // values (it upper-cases the payload first, so any casing is accepted here).
    private static readonly HashSet<string> ChargeCurrentLimits =
        new(["6A", "8A", "16A", "MAX"], StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Command> ByName = new(StringComparer.Ordinal)
    {
        ["climate"] = new("climate/remoteClimateState", value => OnOff("climate", value)),
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
        // The SAIC API expects a JSON blob (mode + start/end time), whose shape isn't validated
        // here; only the length forwarded to MQTT is capped.
        ["scheduled-charging"] = new("drivetrain/chargingSchedule", value =>
            value.Length <= 500 ? null : "value is too long"),
        ["lock"] = new("doors/locked", value =>
            value is "True" or "False" ? null : "'lock' value must be 'True' or 'False'"),
        ["refresh"] = new("refresh/mode", value =>
            value == "force" ? null : "'refresh' value must be 'force'"),
    };

    private static readonly Dictionary<string, string> CommandsByTopic =
        ByName.ToDictionary(pair => pair.Value.Topic, pair => pair.Key, StringComparer.Ordinal);

    private static string? OnOff(string command, string value) =>
        value is "on" or "off" ? null : $"'{command}' value must be 'on' or 'off'";

    private static string? IntegerBetween(string command, string value, int min, int max) =>
        int.TryParse(value, out var number) && number >= min && number <= max
            ? null
            : $"'{command}' value must be an integer between {min} and {max}";

    /// <summary>The gateway topic for <paramref name="command"/>, or null for a command the API does not know.</summary>
    internal static string? TopicFor(string command) => ByName.GetValueOrDefault(command)?.Topic;

    /// <summary>
    /// The command sent on <paramref name="topic"/>, or null when it is not one the API sends,
    /// such as a command Home Assistant sent through the same broker.
    /// </summary>
    internal static string? CommandFor(string topic) => CommandsByTopic.GetValueOrDefault(topic);

    /// <summary>
    /// Why the gateway would refuse <paramref name="value"/> for <paramref name="command"/>, or
    /// null when it takes it. A command the API does not know is refused by <see cref="TopicFor"/>.
    /// </summary>
    internal static ValidationError? Validate(string command, string value) =>
        ByName.GetValueOrDefault(command)?.Check(value) is { } message
            ? new ValidationError("command.invalidValue", message)
            : null;
}
