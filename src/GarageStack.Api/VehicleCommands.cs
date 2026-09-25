namespace GarageStack.Api;

/// <summary>
/// The commands the API accepts, each with the gateway topic it travels on. A command is published
/// on {topic}/set and the gateway answers on {topic}/result, so one table serves both directions:
/// sending a command, and naming the command an answer belongs to.
/// </summary>
internal static class VehicleCommands
{
    // Topics as saic-python-mqtt-gateway's handlers register them (src/mqtt_topics.py).
    private static readonly Dictionary<string, string> TopicsByCommand = new(StringComparer.Ordinal)
    {
        ["climate"] = "climate/remoteClimateState",
        ["climate-temperature"] = "climate/remoteTemperature",
        ["rear-defroster"] = "climate/rearWindowDefrosterHeating",
        ["seat-left"] = "climate/heatedSeatsFrontLeftLevel",
        ["seat-right"] = "climate/heatedSeatsFrontRightLevel",
        ["find-my-car"] = "location/findMyCar",
        ["charge-limit"] = "drivetrain/chargeCurrentLimit",
        ["scheduled-charging"] = "drivetrain/chargingSchedule",
        ["lock"] = "doors/locked",
        ["refresh"] = "refresh/mode",
    };

    private static readonly Dictionary<string, string> CommandsByTopic =
        TopicsByCommand.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>The gateway topic for <paramref name="command"/>, or null for a command the API does not know.</summary>
    internal static string? TopicFor(string command) => TopicsByCommand.GetValueOrDefault(command);

    /// <summary>
    /// The command sent on <paramref name="topic"/>, or null when it is not one the API sends,
    /// such as a command Home Assistant sent through the same broker.
    /// </summary>
    internal static string? CommandFor(string topic) => CommandsByTopic.GetValueOrDefault(topic);
}
