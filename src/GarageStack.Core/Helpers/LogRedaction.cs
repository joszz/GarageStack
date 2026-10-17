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

    /// <summary>
    /// A request path with each VIN segment shortened, for the per-request log line: every
    /// /api/vehicles/{vin}/... route would otherwise write the full VIN at Information level.
    /// </summary>
    public static string RequestPath(string path) => VinFormat.PathSegment().Replace(path, m => Vin(m.Value));

    /// <summary>
    /// Text from outside the server (a browser's error message or stack trace) with every VIN in it
    /// shortened, wherever it sits: after a path, inside a sentence, before a query string.
    /// </summary>
    public static string Vins(string text) => VinFormat.InText().Replace(text, m => Vin(m.Value));

    /// <summary>
    /// An account name for a sign-in log line. A username or display name is kept, so the log
    /// still says which household member signed in; one that is an email address (providers fall
    /// back to it) keeps only its first character and its domain.
    /// </summary>
    public static string Account(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "***";
        var at = name.IndexOf('@');
        return at <= 0 ? name : $"{name[0]}***{name[at..]}";
    }

    /// <summary>
    /// Only the host of a URL. A push subscription's endpoint is a bearer-like address for one
    /// device; its host (the browser's push service) is all a log line needs.
    /// </summary>
    public static string UrlHost(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "***";

    /// <summary>Redacts the account email and VIN segments of a saic-mqtt-gateway topic.</summary>
    public static string MqttTopic(string topic)
    {
        var sanitized = topic.ReplaceLineEndings(" ");
        return SaicTopicPrefix().Replace(sanitized, m => $"saic/***/vehicles/{Vin(m.Groups[1].Value)}/");
    }
}
