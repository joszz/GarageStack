using System.Text.RegularExpressions;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Keeps personal identifiers out of the rotating log files. MQTT topics carry the MG account
/// email and the VIN (saic/{email}/vehicles/{vin}/...), and the VIN on its own still identifies
/// one specific car, so both are shortened before they reach a log line at Information level
/// or above. The full values remain available at Debug level where the callers choose to log them.
/// </summary>
public static partial class LogRedaction
{
    private const int VinVisibleSuffix = 4;

    [GeneratedRegex(@"^saic/[^/]+/vehicles/([^/]+)/")]
    private static partial Regex SaicTopicPrefix();

    /// <summary>Keeps only the last few characters of a VIN, enough to tell two cars apart.</summary>
    public static string Vin(string? vin)
    {
        if (string.IsNullOrEmpty(vin)) return "***";
        return vin.Length <= VinVisibleSuffix
            ? new string('*', vin.Length)
            : $"***{vin[^VinVisibleSuffix..]}";
    }

    /// <summary>Redacts the account email and VIN segments of a saic-mqtt-gateway topic.</summary>
    public static string MqttTopic(string topic)
    {
        var sanitized = topic.ReplaceLineEndings(" ");
        return SaicTopicPrefix().Replace(sanitized, m => $"saic/***/vehicles/{Vin(m.Groups[1].Value)}/");
    }
}
