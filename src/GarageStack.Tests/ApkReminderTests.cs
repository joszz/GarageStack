using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

// An APK date is a day on the Dutch calendar, so a reminder counts calendar days in Amsterdam:
// the night the clocks change is as long as any other.
public class ApkReminderTests
{
    [Theory]
    [InlineData(31, ApkStage.None)]
    [InlineData(30, ApkStage.DueWithinMonth)]
    [InlineData(8, ApkStage.DueWithinMonth)]
    [InlineData(7, ApkStage.DueWithinWeek)]
    [InlineData(1, ApkStage.DueWithinWeek)]
    // The expiry date is the last day the car may drive on.
    [InlineData(0, ApkStage.DueWithinWeek)]
    [InlineData(-1, ApkStage.Expired)]
    [InlineData(-400, ApkStage.Expired)]
    public void StageOf_Thresholds(int daysLeft, ApkStage expected)
    {
        Assert.Equal(expected, ApkReminder.StageOf(daysLeft));
    }

    // The clocks go back at 03:00 on 25 October 2026, so that day has 25 hours: a count in 24-hour
    // spans would come out an hour off around it.
    [Theory]
    [InlineData("2026-10-24T21:59:00Z", 8)] // 23:59 on the 24th, summer time
    [InlineData("2026-10-24T22:00:00Z", 7)] // midnight on the 25th, summer time
    [InlineData("2026-10-25T00:30:00Z", 7)] // 02:30 the first time round
    [InlineData("2026-10-25T01:30:00Z", 7)] // 02:30 the second time round
    [InlineData("2026-10-25T22:59:00Z", 7)] // 23:59 on the 25th, winter time
    [InlineData("2026-10-25T23:00:00Z", 6)] // midnight on the 26th
    public void DaysLeft_CountsCalendarDaysAcrossTheAutumnChange(string utcNow, int expected)
    {
        var expiresOn = new DateOnly(2026, 11, 1);

        Assert.Equal(expected, ApkReminder.DaysLeft(expiresOn, Utc(utcNow)));
    }

    // The clocks go forward at 02:00 on 29 March 2026: that day has 23 hours.
    [Theory]
    [InlineData("2026-03-28T22:59:00Z", 31)] // 23:59 on the 28th, winter time
    [InlineData("2026-03-28T23:00:00Z", 30)] // midnight on the 29th
    [InlineData("2026-03-29T21:59:00Z", 30)] // 23:59 on the 29th, summer time
    [InlineData("2026-03-29T22:00:00Z", 29)] // midnight on the 30th
    public void DaysLeft_CountsCalendarDaysAcrossTheSpringChange(string utcNow, int expected)
    {
        var expiresOn = new DateOnly(2026, 4, 28);

        Assert.Equal(expected, ApkReminder.DaysLeft(expiresOn, Utc(utcNow)));
    }

    [Fact]
    public void DaysLeft_OnTheLastDay_IsZero_AndTheDayAfter_IsExpired()
    {
        var expiresOn = new DateOnly(2026, 10, 30);

        Assert.Equal(0, ApkReminder.DaysLeft(expiresOn, Utc("2026-10-30T21:00:00Z")));
        Assert.Equal(ApkStage.Expired, ApkReminder.StageOf(ApkReminder.DaysLeft(expiresOn, Utc("2026-10-30T23:00:00Z"))));
    }

    private static DateTime Utc(string instant) =>
        DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;
}
