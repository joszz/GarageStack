using System.Globalization;

namespace GarageStack.Core.Helpers;

/// <summary>A time of day as the API, the gateway's schedules and the notifications write it: 24-hour HH:mm.</summary>
public static class TimeOfDayText
{
    public const string Format = "HH:mm";

    /// <remarks>
    /// Since .NET 11 the parser reads "24:00" as midnight. The gateway and the car only know 00 to
    /// 23, so a time is only taken when it writes back as the same text.
    /// </remarks>
    public static bool TryParse(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
        && ToText(time) == text;

    public static string ToText(TimeOnly time) => time.ToString(Format, CultureInfo.InvariantCulture);
}
