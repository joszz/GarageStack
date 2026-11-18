using System.Globalization;
using System.Text;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Writes an iCalendar (RFC 5545) document of whole-day and timed events, for calendar apps that
/// subscribe to a feed. Only what such a feed needs: no time zones (timed events are in UTC, which
/// every app shows in its own zone), no recurrence and no alarms, which subscribed calendars mostly
/// ignore anyway.
/// </summary>
public sealed class IcsCalendar(string name)
{
    // RFC 5545 3.1: a content line is folded at 75 octets.
    private const int MaxLineOctets = 75;

    private readonly List<string[]> _events = [];

    /// <summary>
    /// How often an app should fetch the feed again. A hint: Google Calendar keeps its own pace,
    /// Apple's and Thunderbird's follow it.
    /// </summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>An event that takes the whole of <paramref name="date"/>.</summary>
    public void AddAllDay(string uid, DateOnly date, string summary, string? description = null) =>
        _events.Add(
        [
            $"UID:{Escape(uid)}",
            $"DTSTART;VALUE=DATE:{Date(date)}",
            $"DTEND;VALUE=DATE:{Date(date.AddDays(1))}",
            $"SUMMARY:{Escape(summary)}",
            .. Description(description),
            "TRANSP:TRANSPARENT",
        ]);

    /// <summary>An event from <paramref name="startUtc"/> for <paramref name="duration"/>.</summary>
    public void AddTimed(string uid, DateTime startUtc, TimeSpan duration, string summary, string? description = null) =>
        _events.Add(
        [
            $"UID:{Escape(uid)}",
            $"DTSTART:{Utc(startUtc)}",
            $"DTEND:{Utc(startUtc + duration)}",
            $"SUMMARY:{Escape(summary)}",
            .. Description(description),
        ]);

    /// <summary>The document, stamped <paramref name="nowUtc"/>, with CRLF line ends as the format requires.</summary>
    public string Write(DateTime nowUtc)
    {
        var refresh = $"PT{(int)RefreshInterval.TotalMinutes}M";
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//GarageStack//Calendar feed//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            $"X-WR-CALNAME:{Escape(name)}",
            $"NAME:{Escape(name)}",
            $"REFRESH-INTERVAL;VALUE=DURATION:{refresh}",
            $"X-PUBLISHED-TTL:{refresh}",
        };
        foreach (var e in _events)
        {
            lines.Add("BEGIN:VEVENT");
            lines.Add($"DTSTAMP:{Utc(nowUtc)}");
            lines.AddRange(e);
            lines.Add("END:VEVENT");
        }
        lines.Add("END:VCALENDAR");

        var text = new StringBuilder();
        foreach (var line in lines) Fold(text, line);
        return text.ToString();
    }

    /// <summary>RFC 5545 3.3.11: backslash, semicolon, comma and line breaks are escaped in text.</summary>
    internal static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", @"\;", StringComparison.Ordinal)
            .Replace(",", @"\,", StringComparison.Ordinal)
            .Replace("\r\n", @"\n", StringComparison.Ordinal)
            .Replace("\n", @"\n", StringComparison.Ordinal)
            .Replace("\r", @"\n", StringComparison.Ordinal);

    // A longer line goes on in lines that start with a space, never splitting a character's bytes.
    private static void Fold(StringBuilder text, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > MaxLineOctets)
            {
                text.Append("\r\n ");
                octets = 1;
            }
            text.Append(rune.ToString());
            octets += size;
        }
        text.Append("\r\n");
    }

    private static IEnumerable<string> Description(string? description) =>
        string.IsNullOrWhiteSpace(description) ? [] : [$"DESCRIPTION:{Escape(description)}"];

    private static string Date(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Utc(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
}
