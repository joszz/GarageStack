using System.Text;
using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

// The calendar feed's document, as RFC 5545 has it: what a calendar app reads.
public class IcsCalendarTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

    private static string[] Lines(string ics) => ics.Split("\r\n");

    [Fact]
    public void EmptyCalendar_IsAValidDocumentWithItsName()
    {
        var ics = new IcsCalendar("GarageStack").Write(Now);

        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.Contains("X-WR-CALNAME:GarageStack", Lines(ics));
        Assert.Contains("REFRESH-INTERVAL;VALUE=DURATION:PT360M", Lines(ics));
        Assert.DoesNotContain("BEGIN:VEVENT", ics);
    }

    [Fact]
    public void AllDayEvent_TakesTheWholeDate()
    {
        var calendar = new IcsCalendar("GarageStack");
        calendar.AddAllDay("apk-1-20270301@garagestack", new DateOnly(2027, 3, 1), "APK expires: AB123C");

        var lines = Lines(calendar.Write(Now));

        Assert.Contains("BEGIN:VEVENT", lines);
        Assert.Contains("UID:apk-1-20270301@garagestack", lines);
        Assert.Contains("DTSTAMP:20261118T100000Z", lines);
        Assert.Contains("DTSTART;VALUE=DATE:20270301", lines);
        Assert.Contains("DTEND;VALUE=DATE:20270302", lines);
        Assert.Contains("SUMMARY:APK expires: AB123C", lines);
        Assert.Contains("TRANSP:TRANSPARENT", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("DESCRIPTION", StringComparison.Ordinal));
    }

    [Fact]
    public void TimedEvent_IsInUtc()
    {
        var calendar = new IcsCalendar("GarageStack");
        calendar.AddTimed("climate-2@garagestack", new DateTime(2026, 11, 19, 6, 30, 0, DateTimeKind.Utc),
            TimeSpan.FromMinutes(15), "Climate on", "21 °C");

        var lines = Lines(calendar.Write(Now));

        Assert.Contains("DTSTART:20261119T063000Z", lines);
        Assert.Contains("DTEND:20261119T064500Z", lines);
        Assert.Contains("DESCRIPTION:21 °C", lines);
    }

    [Theory]
    [InlineData("Oil, filter; brakes", @"Oil\, filter\; brakes")]
    [InlineData(@"C:\garage", @"C:\\garage")]
    [InlineData("line one\nline two", @"line one\nline two")]
    [InlineData("line one\r\nline two", @"line one\nline two")]
    public void Text_IsEscaped(string text, string escaped) =>
        Assert.Equal(escaped, IcsCalendar.Escape(text));

    [Fact]
    public void LongLines_AreFoldedAt75Octets_WithoutSplittingACharacter()
    {
        var calendar = new IcsCalendar("GarageStack");
        var summary = string.Concat(Enumerable.Repeat("Distribution belt é ", 10));
        calendar.AddAllDay("m-1@garagestack", new DateOnly(2027, 1, 1), summary);

        var ics = calendar.Write(Now);

        foreach (var line in Lines(ics))
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"Line of {Encoding.UTF8.GetByteCount(line)} octets: {line}");
        var unfolded = ics.Replace("\r\n ", "", StringComparison.Ordinal);
        Assert.Contains($"SUMMARY:{summary}", unfolded);
    }
}
