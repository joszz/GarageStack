using System.Globalization;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;

namespace GarageStack.Api.Services;

/// <summary>
/// The calendar feed's events: the APK expiry date, the date each maintenance item comes due by
/// time, and the climate schedule runs of the next two weeks. Built afresh on every fetch, so a
/// calendar app always has what GarageStack knows now.
/// </summary>
internal static class CalendarFeed
{
    /// <summary>How far ahead climate schedule runs are listed; the app fetches again long before.</summary>
    internal static readonly TimeSpan ClimateHorizon = TimeSpan.FromDays(14);

    private static readonly TimeSpan ClimateRunLength = TimeSpan.FromMinutes(15);

    /// <summary>One car and what the feed reads of it.</summary>
    internal sealed record Car(
        int VehicleId,
        RdwVehicleInfo? Rdw,
        IReadOnlyList<MaintenanceItem> Maintenance,
        IReadOnlyList<ClimateSchedule> ClimateSchedules);

    public static string Build(IEnumerable<Car> cars, DateTime nowUtc, IStringLocalizer<CalendarStrings> strings)
    {
        var calendar = new IcsCalendar(strings["CalendarName"]);
        foreach (var car in cars)
        {
            AddApk(calendar, car, strings);
            AddMaintenance(calendar, car, nowUtc, strings);
            AddClimate(calendar, car, nowUtc, strings);
        }
        return calendar.Write(nowUtc);
    }

    private static void AddApk(IcsCalendar calendar, Car car, IStringLocalizer<CalendarStrings> strings)
    {
        if (car.Rdw is not { Found: true, ApkExpiresOn: { } expiresOn } rdw) return;
        calendar.AddAllDay(Uid("apk", car.VehicleId, expiresOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
            expiresOn, strings["ApkExpires", rdw.LicensePlate]);
    }

    // Only a time interval gives a date; an item due by distance alone has none to show.
    private static void AddMaintenance(IcsCalendar calendar, Car car, DateTime nowUtc, IStringLocalizer<CalendarStrings> strings)
    {
        foreach (var item in car.Maintenance)
        {
            var due = MaintenanceDueCalculator.Calculate(
                item.IntervalKm, item.IntervalMonths, item.LastServiceDate, item.LastServiceOdometerKm, null, nowUtc);
            if (due.NextDueDate is not { } dueAt) continue;

            var date = DateOnly.FromDateTime(dueAt);
            calendar.AddAllDay(Uid("maintenance", item.Id, date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
                date, strings["MaintenanceDue", item.Name], item.Notes);
        }
    }

    private static void AddClimate(IcsCalendar calendar, Car car, DateTime nowUtc, IStringLocalizer<CalendarStrings> strings)
    {
        var until = nowUtc + ClimateHorizon;
        foreach (var schedule in car.ClimateSchedules)
        {
            if (!schedule.Enabled || !TimeZoneInfo.TryFindSystemTimeZoneById(schedule.TimeZoneId, out var zone)) continue;

            var summary = string.IsNullOrWhiteSpace(schedule.Name)
                ? strings["Climate"].Value
                : strings["ClimateNamed", schedule.Name].Value;
            var description = Describe(schedule, strings);
            foreach (var run in Runs(schedule, zone, nowUtc, until))
            {
                calendar.AddTimed(Uid("climate", schedule.Id, run.ToString("yyyyMMdd'T'HHmm", CultureInfo.InvariantCulture)),
                    run, ClimateRunLength, summary, description);
            }
        }
    }

    /// <summary>
    /// The runs after <paramref name="nowUtc"/> up to <paramref name="untilUtc"/>. A schedule without
    /// weekdays runs once, at the time it holds; a weekly one at every run up to the horizon.
    /// </summary>
    internal static IEnumerable<DateTime> Runs(ClimateSchedule schedule, TimeZoneInfo zone, DateTime nowUtc, DateTime untilUtc)
    {
        if (schedule.Days == ClimateScheduleDays.None)
        {
            if (schedule.NextRunUtc is { } once && once > nowUtc && once <= untilUtc) yield return once;
            yield break;
        }

        for (var run = ClimateScheduleCalendar.NextRunUtc(schedule.StartTime, schedule.Days, zone, nowUtc);
             run <= untilUtc;
             run = ClimateScheduleCalendar.NextRunUtc(schedule.StartTime, schedule.Days, zone, run))
        {
            yield return run;
        }
    }

    private static string Describe(ClimateSchedule schedule, IStringLocalizer<CalendarStrings> strings)
    {
        string mode = schedule.Mode switch
        {
            ClimateScheduleMode.FanOnly => strings["ClimateFanOnly"],
            ClimateScheduleMode.FrontDefrost => strings["ClimateFrontDefrost"],
            _ => strings["ClimateTemperature", schedule.TemperatureC],
        };
        return schedule.OnlyBelowC is null && schedule.OnlyAboveC is null
            ? mode
            : $"{mode}\n{strings["ClimateConditional"]}";
    }

    // Stable per event, so an app updates an event it has rather than adding it again; a new date
    // is a new event, and the old one leaves with the next fetch.
    private static string Uid(string kind, int id, string when) =>
        string.Create(CultureInfo.InvariantCulture, $"{kind}-{id}-{when}@garagestack");
}
