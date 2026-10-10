using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// When a climate schedule runs next. A schedule is a time on the clock in its own time zone, so
/// the UTC instant moves with daylight saving time while 07:30 stays 07:30.
/// </summary>
public static class ClimateScheduleCalendar
{
    /// <summary>
    /// The first run strictly after <paramref name="afterUtc"/>: on one of <paramref name="days"/>,
    /// or on any day when none is given (a schedule that runs once). Strictly after, so a run that
    /// was just claimed is never handed out again.
    /// </summary>
    public static DateTime NextRunUtc(TimeOnly start, ClimateScheduleDays days, TimeZoneInfo zone, DateTime afterUtc)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(afterUtc, zone));
        // Today when the time is still ahead, else within the next seven days; the eighth day
        // covers a schedule for today's weekday whose time has already passed.
        for (var i = 0; i <= 7; i++)
        {
            var date = today.AddDays(i);
            if (days != ClimateScheduleDays.None && !days.HasFlag(date.DayOfWeek.Flag())) continue;

            var utc = WallClockToUtc(date.ToDateTime(start), zone);
            if (utc > afterUtc) return utc;
        }

        throw new InvalidOperationException("A weekly schedule always runs within eight days.");
    }

    /// <summary>
    /// The UTC instant of a time on the clock in <paramref name="zone"/>. A time that falls in the
    /// spring-forward gap (02:30 when clocks jump from 02:00 to 03:00) runs as late as it was skipped,
    /// at 03:30. A time that happens twice when clocks fall back runs the first time only.
    /// </summary>
    internal static DateTime WallClockToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // The offset from before the jump: the gap is as long as the jump, so this lands just past it.
        if (zone.IsInvalidTime(local))
            return DateTime.SpecifyKind(local - zone.GetUtcOffset(local.AddDays(-1)), DateTimeKind.Utc);

        // The larger offset is the one in force before the clocks went back: the first occurrence.
        if (zone.IsAmbiguousTime(local))
            return DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
