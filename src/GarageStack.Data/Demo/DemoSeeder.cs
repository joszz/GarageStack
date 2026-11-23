using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Demo;

/// <summary>
/// Fills the in-memory demo database with the demo cars and, for each, a handful of maintenance
/// items in various due states, climate schedules and the weekly summary the Worker would have sent,
/// so those pages and their dashboard cards have something to show. The first car also gets an RDW
/// lookup (with the two notifications the Worker would have sent about it); the second has no plate,
/// so its RDW card shows the field to enter one. Each car's differ, so switching cars shows it.
/// Telemetry and trips come from the in-memory demo repositories instead.
/// </summary>
public static class DemoSeeder
{
    /// <param name="db">The in-memory demo database.</param>
    /// <param name="rdwEnabled">Whether to seed the RDW lookup and its notifications: not when RDW_ENABLED=false.</param>
    /// <param name="demo">Which cars to seed; the one ZS EV when null.</param>
    /// <param name="ct">Cancels the seeding.</param>
    public static async Task SeedAsync(
        AppDbContext db, bool rdwEnabled = true, DemoOptions? demo = null, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        var vehicles = (demo ?? new DemoOptions(SecondVehicle: false)).Vehicles;
        if (!await db.Vehicles.AnyAsync(ct))
        {
            db.Vehicles.AddRange(vehicles);
            await db.SaveChangesAsync(ct);
        }

        if (!await db.ClimateSchedules.AnyAsync(ct))
            await SeedClimateSchedulesAsync(db, vehicles, ct);

        if (rdwEnabled && !await db.RdwVehicleInfos.AnyAsync(ct))
            await SeedRdwAsync(db, ct);

        foreach (var vehicle in vehicles)
        {
            if (!await db.AppNotifications.AnyAsync(n => n.Category == NotificationCategories.WeeklySummary && n.VehicleId == vehicle.Id, ct))
                await SeedWeeklySummaryAsync(db, DemoCars.For(vehicle.Id), ct);
        }

        if (await db.MaintenanceItems.AnyAsync(ct))
            return;

        await SeedZsEvMaintenanceAsync(db, ct);
        if (vehicles.Any(v => v.Id == DemoVehicleRepository.SecondDemoVehicle.Id))
            await SeedHsPhevMaintenanceAsync(db, ct);
    }

    private static async Task SeedZsEvMaintenanceAsync(AppDbContext db, CancellationToken ct)
    {
        var vehicleId = DemoVehicleRepository.DemoVehicle.Id;
        var oilChange = new MaintenanceItem
        {
            VehicleId = vehicleId,
            Name = "Oil change",
            IntervalKm = 15_000,
            IntervalMonths = 12,
            LastServiceDate = DateTime.UtcNow.AddMonths(-7),
            LastServiceOdometerKm = 15_000,
        };
        var tyreRotation = new MaintenanceItem
        {
            VehicleId = vehicleId,
            Name = "Tyre rotation",
            IntervalKm = 10_000,
            LastServiceDate = DateTime.UtcNow.AddMonths(-4),
            LastServiceOdometerKm = 15_500,
        };
        var majorService = new MaintenanceItem
        {
            VehicleId = vehicleId,
            Name = "Major service",
            IntervalKm = 60_000,
            IntervalMonths = 60,
            LastServiceDate = DateTime.UtcNow.AddMonths(-61),
            LastServiceOdometerKm = 0,
        };
        var cabinFilter = new MaintenanceItem
        {
            VehicleId = vehicleId,
            Name = "Cabin air filter",
            IntervalKm = 15_000,
        };

        db.MaintenanceItems.AddRange(oilChange, tyreRotation, majorService, cabinFilter);
        await db.SaveChangesAsync(ct);

        // One log entry per item that has a service history, mirroring its baseline, with what it
        // cost so the costs card has something to add up; the oil change also has the one before.
        await AddServiceLogAsync(db, new Dictionary<MaintenanceItem, decimal>
        {
            [oilChange] = 129.50m,
            [tyreRotation] = 45m,
            [majorService] = 685m,
        }, ct);
        db.MaintenanceLogEntries.Add(new MaintenanceLogEntry
        {
            MaintenanceItemId = oilChange.Id,
            PerformedAt = oilChange.LastServiceDate!.Value.AddMonths(-12),
            OdometerKm = oilChange.LastServiceOdometerKm - 15_000,
            Cost = 118.75m,
        });
        await db.SaveChangesAsync(ct);
    }

    // The plug-in hybrid has an engine to look after as well, and fewer kilometres on it: its
    // service is nearly due by the calendar, its brake fluid overdue.
    private static async Task SeedHsPhevMaintenanceAsync(AppDbContext db, CancellationToken ct)
    {
        var vehicleId = DemoVehicleRepository.SecondDemoVehicle.Id;
        var service = new MaintenanceItem
        {
            VehicleId = vehicleId,
            Name = "Annual service",
            IntervalKm = 20_000,
            IntervalMonths = 12,
            LastServiceDate = DateTime.UtcNow.AddMonths(-11),
            LastServiceOdometerKm = 6_100,
        };
        var brakeFluid = new MaintenanceItem
        {
            VehicleId = vehicleId,
            Name = "Brake fluid",
            IntervalMonths = 24,
            LastServiceDate = DateTime.UtcNow.AddMonths(-25),
            LastServiceOdometerKm = 1_200,
        };
        var wiperBlades = new MaintenanceItem
        {
            VehicleId = vehicleId,
            Name = "Wiper blades",
            IntervalMonths = 12,
            LastServiceDate = DateTime.UtcNow.AddMonths(-2),
            LastServiceOdometerKm = 16_900,
        };

        db.MaintenanceItems.AddRange(service, brakeFluid, wiperBlades);
        await db.SaveChangesAsync(ct);

        await AddServiceLogAsync(db, new Dictionary<MaintenanceItem, decimal>
        {
            [service] = 349m,
            [brakeFluid] = 79m,
            [wiperBlades] = 32.95m,
        }, ct);
    }

    private static async Task AddServiceLogAsync(AppDbContext db, Dictionary<MaintenanceItem, decimal> costs, CancellationToken ct)
    {
        db.MaintenanceLogEntries.AddRange(
            costs.Select(cost => new MaintenanceLogEntry
            {
                MaintenanceItemId = cost.Key.Id,
                PerformedAt = cost.Key.LastServiceDate!.Value,
                OdometerKm = cost.Key.LastServiceOdometerKm,
                Cost = cost.Value,
            }));
        await db.SaveChangesAsync(ct);
    }

    // What the RDW lookup and the Worker's round after it would have left: the recall seen three
    // days ago and pushed then, and the APK's month notice, sent nine days before the three weeks
    // it has left now. There is no Worker in demo mode, so the texts are the English ones it sends.
    private static async Task SeedRdwAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var vehicle = DemoVehicleRepository.DemoVehicle;
        var registration = DemoRdwClient.Registration(now);
        var seenAt = now.AddDays(-3);
        var recalls = RdwRecalls.Merge([], DemoRdwClient.Statuses, DemoRdwClient.Details, seenAt)
            .Select(r => r.IsOpen ? r with { NotifiedAt = seenAt } : r)
            .ToList();

        db.RdwVehicleInfos.Add(new RdwVehicleInfo
        {
            VehicleId = vehicle.Id,
            LicensePlate = vehicle.LicensePlate!,
            Found = true,
            Make = registration.Make,
            Model = registration.Model,
            ApkExpiresOn = registration.ApkExpiresOn,
            WamInsured = registration.WamInsured,
            OpenRecallIndicator = registration.OpenRecallIndicator,
            RecallsJson = RdwRecalls.Serialize(recalls),
            FetchedAt = now.AddHours(-2),
        });

        var openRecall = recalls.First(r => r.IsOpen);
        db.AppNotifications.AddRange(
            new AppNotification
            {
                Title = "Open recall",
                Body = $"The RDW lists an open recall ({openRecall.Code}): {openRecall.Defect}",
                CreatedAt = seenAt,
                Category = NotificationCategories.RdwRecall(openRecall.Code),
                VehicleId = vehicle.Id,
            },
            new AppNotification
            {
                Title = "APK due soon",
                Body = $"The APK runs out on {registration.ApkExpiresOn:dd-MM-yyyy}, in {ApkReminder.MonthNoticeDays} days.",
                CreatedAt = now.AddDays(-(ApkReminder.MonthNoticeDays - DemoRdwClient.ApkDaysLeft)),
                Category = NotificationCategories.RdwApkDue(ApkReminder.MonthNoticeDays, registration.ApkExpiresOn!.Value),
                VehicleId = vehicle.Id,
            });
        await db.SaveChangesAsync(ct);
    }

    // What the Worker's DrivingSummaryService would have sent this Monday about a demo car's past
    // week, from the same trips and history the trip log and statistics show, in the English it
    // sends. It shows once a device switches the weekly summary on, as a real one does.
    private static async Task SeedWeeklySummaryAsync(AppDbContext db, DemoCar car, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var (from, to) = (monday.AddDays(-7), monday.AddDays(-1));
        var (start, end) = DailyStatsBuilder.UtcRange(from, to, TimeZoneInfo.Utc);

        var builder = new DailyStatsBuilder(TimeZoneInfo.Utc, from, to, today);
        foreach (var snapshot in car.History.Snapshots.Where(s => s.RecordedAt < end))
            builder.Add(TelemetryHistoryPoint.FromSnapshot(snapshot));
        var trips = car.Trips.All
            .Where(t => t.Id is not null && t.StartedAt >= start && t.StartedAt < end)
            .Select(t => t.ToSummary())
            .ToList();
        var figures = DrivingSummary.Build(trips, builder.Build(), VehicleTypeHelper.GetVehicleType(car.Vehicle));
        if (!figures.HasDriving) return;

        var en = System.Globalization.CultureInfo.GetCultureInfo("en");
        List<string> parts = [figures.Trips == 1 ? "1 trip" : $"{figures.Trips} trips", string.Format(en, "{0:N1} kWh", figures.EnergyKwh)];
        if (figures.PerHundredKm is { } perHundred) parts.Add(string.Format(en, "{0:N1} kWh/100 km", perHundred));

        db.AppNotifications.Add(new AppNotification
        {
            Title = string.Format(en, "Your week: {0:N0} km", figures.DistanceKm),
            Body = string.Join(" · ", parts),
            CreatedAt = monday.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc) is var sent && sent < DateTime.UtcNow
                ? sent
                : DateTime.UtcNow,
            Category = NotificationCategories.WeeklySummary,
            VehicleId = car.Vehicle.Id,
        });
        await db.SaveChangesAsync(ct);
    }

    // Nothing runs them in demo mode (there is no Worker), so their next run is only what the
    // page shows. The ZS warms up for work on weekdays and defrosts on frosty mornings; the HS
    // warms up for the weekend.
    private static async Task SeedClimateSchedulesAsync(AppDbContext db, IReadOnlyList<Vehicle> vehicles, CancellationToken ct)
    {
        const string zoneId = "Europe/Amsterdam";
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone)) return;

        var weekdays = ClimateScheduleDays.Monday | ClimateScheduleDays.Tuesday | ClimateScheduleDays.Wednesday
            | ClimateScheduleDays.Thursday | ClimateScheduleDays.Friday;
        List<ClimateSchedule> schedules =
        [
            new()
            {
                VehicleId = DemoVehicleRepository.DemoVehicle.Id,
                Name = "Work",
                StartTime = new TimeOnly(7, 30),
                Days = weekdays,
                TimeZoneId = zoneId,
                Mode = ClimateScheduleMode.On,
                TemperatureC = 21,
                SeatLeftLevel = 2,
                LastRunAt = DateTime.UtcNow.AddDays(-1),
                LastRunOutcome = ClimateScheduleOutcome.Started,
            },
            new()
            {
                VehicleId = DemoVehicleRepository.DemoVehicle.Id,
                StartTime = new TimeOnly(8, 15),
                Days = ClimateScheduleDays.None,
                TimeZoneId = zoneId,
                Mode = ClimateScheduleMode.FrontDefrost,
                RearDefroster = true,
                OnlyBelowC = 3,
            },
        ];
        if (vehicles.Any(v => v.Id == DemoVehicleRepository.SecondDemoVehicle.Id))
        {
            schedules.Add(new()
            {
                VehicleId = DemoVehicleRepository.SecondDemoVehicle.Id,
                Name = "Weekend",
                StartTime = new TimeOnly(9, 45),
                Days = ClimateScheduleDays.Saturday | ClimateScheduleDays.Sunday,
                TimeZoneId = zoneId,
                Mode = ClimateScheduleMode.On,
                TemperatureC = 20,
                LastRunAt = DateTime.UtcNow.AddDays(-6),
                LastRunOutcome = ClimateScheduleOutcome.Started,
            });
        }
        foreach (var schedule in schedules)
            schedule.NextRunUtc = ClimateScheduleCalendar.NextRunUtc(schedule.StartTime, schedule.Days, zone, DateTime.UtcNow);

        db.ClimateSchedules.AddRange(schedules);
        await db.SaveChangesAsync(ct);
    }
}
