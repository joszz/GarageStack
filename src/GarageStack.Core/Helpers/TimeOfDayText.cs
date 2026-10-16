using System.Globalization;

namespace GarageStack.Core.Helpers;

/// <summary>A time of day as the API, the gateway's schedules and the notifications write it: 24-hour HH:mm.</summary>
public static class TimeOfDayText
{
    public const string Format = "HH:mm";

    public static bool TryParse(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static string ToText(TimeOnly time) => time.ToString(Format, CultureInfo.InvariantCulture);
}
