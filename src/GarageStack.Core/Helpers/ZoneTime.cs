namespace GarageStack.Core.Helpers;

/// <summary>Times on a local clock, turned into UTC the same way wherever GarageStack needs it.</summary>
public static class ZoneTime
{
    /// <summary>
    /// The UTC instant of a time on the clock in <paramref name="zone"/>. A time that falls in the
    /// spring-forward gap (02:30 when clocks jump from 02:00 to 03:00) counts as late as it was skipped,
    /// 03:30, so a day whose midnight is skipped starts at the first hour that exists. A time that
    /// happens twice when clocks fall back is its first occurrence.
    /// </summary>
    public static DateTime WallClockToUtc(DateTime local, TimeZoneInfo zone)
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

    /// <summary>The UTC instant a local day starts.</summary>
    public static DateTime StartOfDay(DateOnly date, TimeZoneInfo zone) =>
        WallClockToUtc(date.ToDateTime(TimeOnly.MinValue), zone);
}
