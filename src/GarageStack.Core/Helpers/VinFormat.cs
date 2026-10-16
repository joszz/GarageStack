using System.Text.RegularExpressions;

namespace GarageStack.Core.Helpers;

/// <summary>
/// What a VIN looks like: exactly 17 letters and digits, without I, O and Q (ISO 3779). The one
/// definition behind every place that has to recognise one: MQTT topics, Home Assistant discovery
/// identifiers, and request paths whose VIN is kept out of the log.
/// </summary>
public static partial class VinFormat
{
    [GeneratedRegex(@"^[A-HJ-NPR-Z0-9]{17}$")]
    private static partial Regex Whole();

    // A path segment that is a VIN: preceded by a slash, followed by a slash or the end.
    [GeneratedRegex(@"(?<=/)[A-HJ-NPR-Z0-9]{17}(?=/|$)")]
    internal static partial Regex PathSegment();

    public static bool IsValid(string? value) => value is not null && Whole().IsMatch(value);
}
