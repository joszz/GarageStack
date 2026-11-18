using System.Globalization;
using GarageStack.Api;
using GarageStack.Api.Services;
using GarageStack.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace GarageStack.Tests;

// What the calendar feed lists: the APK, maintenance due by date, and two weeks of climate runs.
public class CalendarFeedTests
{
    // A Wednesday, 10:00 in Amsterdam.
    private static readonly DateTime Now = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);

    private static IStringLocalizer<CalendarStrings> Strings() =>
        new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Resources")
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<CalendarStrings>>();

    private static string Unfolded(string ics) => ics.Replace("\r\n ", "", StringComparison.Ordinal);

    private static string Build(
        RdwVehicleInfo? rdw = null,
        IReadOnlyList<MaintenanceItem>? maintenance = null,
        IReadOnlyList<ClimateSchedule>? schedules = null) =>
        Unfolded(CalendarFeed.Build([new CalendarFeed.Car(1, rdw, maintenance ?? [], schedules ?? [])], Now, Strings()));

    private static ClimateSchedule Weekdays(int id = 3) => new()
    {
        Id = id,
        VehicleId = 1,
        Name = "Work",
        StartTime = new TimeOnly(7, 30),
        Days = ClimateScheduleDays.Monday | ClimateScheduleDays.Tuesday | ClimateScheduleDays.Wednesday
            | ClimateScheduleDays.Thursday | ClimateScheduleDays.Friday,
        TimeZoneId = "Europe/Amsterdam",
        TemperatureC = 21,
    };

    [Fact]
    public void Apk_IsAnAllDayEventOnItsExpiryDate()
    {
        var ics = Build(rdw: new RdwVehicleInfo { VehicleId = 1, LicensePlate = "AB123C", Found = true, ApkExpiresOn = new DateOnly(2027, 3, 1) });

        Assert.Contains("UID:apk-1-20270301@garagestack", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20270301", ics);
        Assert.Contains("SUMMARY:APK expires: AB123C", ics);
    }

    [Fact]
    public void Apk_IsLeftOutWhenTheRegistrationWasNotFound()
    {
        var ics = Build(rdw: new RdwVehicleInfo { VehicleId = 1, LicensePlate = "AB123C", Found = false, ApkExpiresOn = new DateOnly(2027, 3, 1) });

        Assert.DoesNotContain("apk-", ics);
    }

    [Fact]
    public void Maintenance_DueByTime_IsOnItsDueDate_WithItsNotes()
    {
        var ics = Build(maintenance:
        [
            new MaintenanceItem { Id = 5, Name = "Oil", Notes = "5W-30", IntervalMonths = 12, LastServiceDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
            new MaintenanceItem { Id = 6, Name = "Tyres", IntervalKm = 40000, LastServiceOdometerKm = 10000 },
        ]);

        Assert.Contains("UID:maintenance-5-20270601@garagestack", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20270601", ics);
        Assert.Contains("SUMMARY:Maintenance due: Oil", ics);
        Assert.Contains("DESCRIPTION:5W-30", ics);
        // Due by distance alone: no date to put it on.
        Assert.DoesNotContain("Tyres", ics);
    }

    [Fact]
    public void WeeklyClimateSchedule_ListsEveryRunOfTheNextTwoWeeks_InUtc()
    {
        var ics = Build(schedules: [Weekdays()]);

        // Thursday 19 November 07:30 in Amsterdam is 06:30 UTC; today's 07:30 has passed.
        Assert.Contains("DTSTART:20261119T063000Z", ics);
        Assert.DoesNotContain("DTSTART:20261118T063000Z", ics);
        Assert.Equal(10, ics.Split("SUMMARY:Climate on: Work").Length - 1);
        Assert.Contains("DESCRIPTION:21 °C", ics);
    }

    [Fact]
    public void ClimateRuns_FollowDaylightSavingTime()
    {
        var schedule = Weekdays();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var march = new DateTime(2027, 3, 24, 12, 0, 0, DateTimeKind.Utc);

        var runs = CalendarFeed.Runs(schedule, zone, march, march + CalendarFeed.ClimateHorizon).ToList();

        // 07:30 stays 07:30: 06:30 UTC before the clocks go forward on 28 March, 05:30 after.
        Assert.Contains(new DateTime(2027, 3, 25, 6, 30, 0, DateTimeKind.Utc), runs);
        Assert.Contains(new DateTime(2027, 3, 29, 5, 30, 0, DateTimeKind.Utc), runs);
    }

    [Fact]
    public void OneOffClimateSchedule_IsListedOnce_AndADisabledOneNotAtAll()
    {
        var once = new ClimateSchedule
        {
            Id = 8,
            VehicleId = 1,
            StartTime = new TimeOnly(8, 0),
            Days = ClimateScheduleDays.None,
            TimeZoneId = "Europe/Amsterdam",
            Mode = ClimateScheduleMode.FrontDefrost,
            NextRunUtc = new DateTime(2026, 11, 20, 7, 0, 0, DateTimeKind.Utc),
            OnlyBelowC = 3,
        };
        var off = Weekdays(9);
        off.Enabled = false;

        var ics = Build(schedules: [once, off]);

        Assert.Contains("UID:climate-8-20261120T0700@garagestack", ics);
        Assert.Contains("SUMMARY:Climate on", ics);
        Assert.Contains(@"DESCRIPTION:Windscreen defrost\nOnly when the outside temperature asks for it", ics);
        Assert.DoesNotContain("climate-9-", ics);
    }

    [Fact]
    public void Texts_FollowTheRequestedLanguage()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("nl");
        try
        {
            var ics = Build(rdw: new RdwVehicleInfo { VehicleId = 1, LicensePlate = "AB123C", Found = true, ApkExpiresOn = new DateOnly(2027, 3, 1) });

            Assert.Contains("SUMMARY:APK verloopt: AB123C", ics);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
