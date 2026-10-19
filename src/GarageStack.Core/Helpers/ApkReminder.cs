namespace GarageStack.Core.Helpers;

public enum ApkStage
{
    /// <summary>More than a month to go, or no APK date known.</summary>
    None,

    /// <summary>30 days or fewer left.</summary>
    DueWithinMonth,

    /// <summary>7 days or fewer left, the last valid day included.</summary>
    DueWithinWeek,

    /// <summary>The last valid day has passed.</summary>
    Expired,
}

/// <summary>
/// When an APK (the Dutch roadworthiness test) is worth a reminder: a month ahead, a week ahead,
/// and once it has run out. The RDW's expiry date is a day on the Dutch calendar, the last one the
/// car may drive on, so days are counted between calendar dates in Amsterdam and never as 24-hour
/// spans: a daylight saving change in between does not move a reminder by a day.
/// </summary>
public static class ApkReminder
{
    public const int MonthNoticeDays = 30;
    public const int WeekNoticeDays = 7;

    private const string DutchZoneId = "Europe/Amsterdam";

    /// <summary>The zone the RDW's dates are days in.</summary>
    public static TimeZoneInfo DutchZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(DutchZoneId);

    /// <summary>Days from today in the Netherlands to the last valid day: 0 on that day, negative once it has passed.</summary>
    public static int DaysLeft(DateOnly expiresOn, DateTime utcNow) =>
        expiresOn.DayNumber - ZoneTime.DateOf(utcNow, DutchZone).DayNumber;

    public static ApkStage StageOf(int daysLeft) => daysLeft switch
    {
        < 0 => ApkStage.Expired,
        <= WeekNoticeDays => ApkStage.DueWithinWeek,
        <= MonthNoticeDays => ApkStage.DueWithinMonth,
        _ => ApkStage.None,
    };
}
