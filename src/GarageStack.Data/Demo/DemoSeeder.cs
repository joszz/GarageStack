using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Demo;

/// <summary>
/// Fills the in-memory demo database with the demo vehicle and a handful of maintenance items
/// in various due states, so the maintenance page and dashboard card have something to show.
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
}
