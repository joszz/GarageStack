using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Demo;

/// <summary>
/// Fills the in-memory demo database with the demo vehicle, a handful of maintenance items in
/// various due states and two climate schedules, so those pages and their dashboard cards have
/// something to show.
/// Telemetry, trips and notifications come from the in-memory demo repositories instead.
/// </summary>
public static class DemoSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        if (!await db.Vehicles.AnyAsync(ct))
        {
            db.Vehicles.Add(DemoVehicleRepository.DemoVehicle);
            await db.SaveChangesAsync(ct);
        }

        if (!await db.ClimateSchedules.AnyAsync(ct))
            await SeedClimateSchedulesAsync(db, ct);

        if (await db.MaintenanceItems.AnyAsync(ct))
            return;

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

        // One log entry per item that has a service history, mirroring its baseline.
        db.MaintenanceLogEntries.AddRange(
            new[] { oilChange, tyreRotation, majorService }.Select(item => new MaintenanceLogEntry
            {
                MaintenanceItemId = item.Id,
                PerformedAt = item.LastServiceDate!.Value,
                OdometerKm = item.LastServiceOdometerKm,
            }));
        await db.SaveChangesAsync(ct);
    }

    // Nothing runs them in demo mode (there is no Worker), so their next run is only what the
    // page shows.
    private static async Task SeedClimateSchedulesAsync(AppDbContext db, CancellationToken ct)
    {
        const string zoneId = "Europe/Amsterdam";
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone)) return;

        var weekdays = ClimateScheduleDays.Monday | ClimateScheduleDays.Tuesday | ClimateScheduleDays.Wednesday
            | ClimateScheduleDays.Thursday | ClimateScheduleDays.Friday;
        var work = new ClimateSchedule
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
        };
        var frostyMorning = new ClimateSchedule
        {
            VehicleId = DemoVehicleRepository.DemoVehicle.Id,
            StartTime = new TimeOnly(8, 15),
            Days = ClimateScheduleDays.None,
            TimeZoneId = zoneId,
            Mode = ClimateScheduleMode.FrontDefrost,
            RearDefroster = true,
            OnlyBelowC = 3,
        };
        foreach (var schedule in new[] { work, frostyMorning })
            schedule.NextRunUtc = ClimateScheduleCalendar.NextRunUtc(schedule.StartTime, schedule.Days, zone, DateTime.UtcNow);

        db.ClimateSchedules.AddRange(work, frostyMorning);
        await db.SaveChangesAsync(ct);
    }
}
