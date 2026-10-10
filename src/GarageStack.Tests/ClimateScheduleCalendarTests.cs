using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class ClimateScheduleCalendarTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly TimeOnly HalfPastSeven = new(7, 30);

    private const ClimateScheduleDays Weekdays = ClimateScheduleDays.Monday | ClimateScheduleDays.Tuesday
        | ClimateScheduleDays.Wednesday | ClimateScheduleDays.Thursday | ClimateScheduleDays.Friday;

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Weekdays_OnAFridayAfterItsTime_RunsMonday()
    {
        // Friday 9 October 2026, 09:00 in Amsterdam (CEST, UTC+2).
        var next = ClimateScheduleCalendar.NextRunUtc(HalfPastSeven, Weekdays, Amsterdam, Utc(2026, 10, 9, 7, 0));

        Assert.Equal(Utc(2026, 10, 12, 5, 30), next);
        Assert.Equal(DateTimeKind.Utc, next.Kind);
    }

    [Fact]
    public void Weekdays_OnAWeekdayBeforeItsTime_RunsToday()
    {
        var next = ClimateScheduleCalendar.NextRunUtc(HalfPastSeven, Weekdays, Amsterdam, Utc(2026, 10, 9, 5, 0));

        Assert.Equal(Utc(2026, 10, 9, 5, 30), next);
    }

    [Fact]
    public void Once_RunsTodayWhileTheTimeIsAhead_ElseTomorrow()
    {
        Assert.Equal(Utc(2026, 10, 9, 5, 30),
            ClimateScheduleCalendar.NextRunUtc(HalfPastSeven, ClimateScheduleDays.None, Amsterdam, Utc(2026, 10, 9, 5, 0)));
        Assert.Equal(Utc(2026, 10, 10, 5, 30),
            ClimateScheduleCalendar.NextRunUtc(HalfPastSeven, ClimateScheduleDays.None, Amsterdam, Utc(2026, 10, 9, 6, 0)));
    }

    [Fact]
    public void ARunAtExactlyTheGivenInstant_IsNotHandedOutAgain()
    {
        var next = ClimateScheduleCalendar.NextRunUtc(HalfPastSeven, Weekdays, Amsterdam, Utc(2026, 10, 9, 5, 30));

        Assert.Equal(Utc(2026, 10, 12, 5, 30), next);
    }

    [Fact]
    public void ASingleWeekday_AfterItsTime_RunsAWeekLater()
    {
        // Friday only, asked on Friday after 07:30: the eighth day is the next Friday.
        var next = ClimateScheduleCalendar.NextRunUtc(HalfPastSeven, ClimateScheduleDays.Friday, Amsterdam, Utc(2026, 10, 9, 7, 0));

        Assert.Equal(Utc(2026, 10, 16, 5, 30), next);
    }

    [Fact]
    public void StaysOnTheClockAcrossTheEndOfSummerTime()
    {
        // Summer time ends on Sunday 25 October 2026: 07:30 is 05:30Z before, 06:30Z after.
        var next = ClimateScheduleCalendar.NextRunUtc(HalfPastSeven, Weekdays, Amsterdam, Utc(2026, 10, 23, 6, 0));

        Assert.Equal(Utc(2026, 10, 26, 6, 30), next);
    }

    [Fact]
    public void ATimeSkippedBySpringForward_RunsJustPastTheGap()
    {
        // Clocks jump from 02:00 to 03:00 on 29 March 2026; 02:30 does not exist and runs at 03:30.
        var next = ClimateScheduleCalendar.NextRunUtc(new TimeOnly(2, 30), ClimateScheduleDays.Sunday, Amsterdam, Utc(2026, 3, 28, 12, 0));

        Assert.Equal(Utc(2026, 3, 29, 1, 30), next);
        Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), TimeZoneInfo.ConvertTimeFromUtc(next, Amsterdam));
    }

    [Fact]
    public void ATimeRepeatedByFallBack_RunsTheFirstTimeOnly()
    {
        // Clocks go back from 03:00 to 02:00 on 25 October 2026; 02:30 happens at 00:30Z and 01:30Z.
        var start = new TimeOnly(2, 30);
        var first = ClimateScheduleCalendar.NextRunUtc(start, ClimateScheduleDays.None, Amsterdam, Utc(2026, 10, 24, 12, 0));
        var afterFirst = ClimateScheduleCalendar.NextRunUtc(start, ClimateScheduleDays.None, Amsterdam, first);

        Assert.Equal(Utc(2026, 10, 25, 0, 30), first);
        Assert.Equal(Utc(2026, 10, 26, 1, 30), afterFirst);
    }

    [Fact]
    public void TheWeekdayIsTheOneOnTheLocalClock()
    {
        // 03:00Z on Saturday is still Friday 23:00 in New York, so a Friday 23:30 schedule runs tonight.
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        var next = ClimateScheduleCalendar.NextRunUtc(new TimeOnly(23, 30), ClimateScheduleDays.Friday, newYork, Utc(2026, 10, 10, 3, 0));

        Assert.Equal(Utc(2026, 10, 10, 3, 30), next);
    }

    [Fact]
    public void AZoneWithoutDaylightSavingTime_KeepsOneOffset()
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

        var next = ClimateScheduleCalendar.NextRunUtc(HalfPastSeven, ClimateScheduleDays.Monday, tokyo, Utc(2026, 10, 9, 0, 0));

        Assert.Equal(Utc(2026, 10, 11, 22, 30), next);
    }

    [Fact]
    public void SouthernHemisphereSpringForward_RunsJustPastTheGap()
    {
        // Sydney jumps from 02:00 to 03:00 on Sunday 4 October 2026 (AEST +10 to AEDT +11).
        var sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

        var next = ClimateScheduleCalendar.NextRunUtc(new TimeOnly(2, 30), ClimateScheduleDays.Sunday, sydney, Utc(2026, 10, 3, 0, 0));

        Assert.Equal(new DateTime(2026, 10, 4, 3, 30, 0), TimeZoneInfo.ConvertTimeFromUtc(next, sydney));
    }
}
