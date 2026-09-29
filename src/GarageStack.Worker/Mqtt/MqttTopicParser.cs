using System.Text.RegularExpressions;

namespace GarageStack.Worker.Mqtt;

/// <summary>The parts of a saic-mqtt-gateway topic: saic/{user}/vehicles/{vin}/{subtopic}.</summary>
public readonly record struct ParsedSaicTopic(string User, string Vin, string Subtopic);

public static partial class MqttTopicParser
{
    // VIN is exactly 17 alphanumeric characters (I, O, Q excluded per ISO 3779).
    [GeneratedRegex(@"^[A-HJ-NPR-Z0-9]{17}$")]
    private static partial Regex VinPattern();

    /// <summary>
    /// Splits a vehicle topic into its user, VIN and subtopic in one pass. Returns false for
    /// anything that is not a saic/{user}/vehicles/{VIN}/... topic with a well-formed VIN.
    /// </summary>
    public static bool TryParse(string topic, out ParsedSaicTopic parsed)
    {
        var parts = topic.Split('/');
        if (parts.Length >= 4 && parts[0] == "saic" && parts[2] == "vehicles" && VinPattern().IsMatch(parts[3]))
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
}
