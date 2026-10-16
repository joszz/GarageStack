using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class ZoneTimeTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // Chile moves its clocks forward at midnight: on 2026-09-06 the day begins at 01:00 local.
    private static readonly TimeZoneInfo Santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

    [Fact]
    public void ADayWhoseMidnightIsSkipped_StartsAtTheFirstHourThatExists()
    {
        Assert.Equal(new DateTime(2026, 9, 6, 4, 0, 0, DateTimeKind.Utc), ZoneTime.StartOfDay(new DateOnly(2026, 9, 6), Santiago));
    }

    [Fact]
    public void ATimeInTheSpringGap_CountsAsLateAsItWasSkipped()
    {
        // 02:30 on 2026-03-29 does not exist in Amsterdam; it runs at 03:30 CEST, 01:30 UTC.
        var utc = ZoneTime.WallClockToUtc(new DateTime(2026, 3, 29, 2, 30, 0), Amsterdam);

        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void ATimeThatHappensTwice_IsItsFirstOccurrence()
    {
        // 02:30 on 2026-10-25 happens in CEST (00:30 UTC) and again in CET (01:30 UTC).
        var utc = ZoneTime.WallClockToUtc(new DateTime(2026, 10, 25, 2, 30, 0), Amsterdam);

        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void AnOrdinaryDay_StartsAtLocalMidnight()
    {
        Assert.Equal(new DateTime(2026, 10, 15, 22, 0, 0, DateTimeKind.Utc), ZoneTime.StartOfDay(new DateOnly(2026, 10, 16), Amsterdam));
    }
}
