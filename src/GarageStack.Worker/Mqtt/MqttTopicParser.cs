using GarageStack.Core.Helpers;

namespace GarageStack.Worker.Mqtt;

/// <summary>The parts of a saic-mqtt-gateway topic: saic/{user}/vehicles/{vin}/{subtopic}.</summary>
public readonly record struct ParsedSaicTopic(string User, string Vin, string Subtopic);

/// <summary>The parts of a gateway account topic: saic/{user}/account/{subtopic}.</summary>
public readonly record struct ParsedAccountTopic(string User, string Subtopic);

public static class MqttTopicParser
{
    /// <summary>
    /// Splits a vehicle topic into its user, VIN and subtopic in one pass. Returns false for
    /// anything that is not a saic/{user}/vehicles/{VIN}/... topic with a well-formed VIN.
    /// </summary>
    public static bool TryParse(string topic, out ParsedSaicTopic parsed)
    {
        var parts = topic.Split('/');
        if (parts.Length >= 4 && parts[0] == "saic" && parts[2] == "vehicles" && VinFormat.IsValid(parts[3]))
        {
            parsed = new ParsedSaicTopic(
                User: parts[1],
                Vin: parts[3],
                Subtopic: parts.Length > 4 ? string.Join('/', parts[4..]) : string.Empty);
            return true;
        }

        parsed = default;
        return false;
    }

    /// <summary>
    /// Splits an account topic (the gateway's sign-in state, shared by every car on the MG
    /// account) into its user and subtopic. Returns false for anything else.
    /// </summary>
    public static bool TryParseAccount(string topic, out ParsedAccountTopic parsed)
    {
        var parts = topic.Split('/');
        if (parts.Length >= 4 && parts[0] == "saic" && parts[2] == "account")
        {
            parsed = new ParsedAccountTopic(User: parts[1], Subtopic: string.Join('/', parts[3..]));
            return true;
        }

        parsed = default;
        return false;
    }
}
