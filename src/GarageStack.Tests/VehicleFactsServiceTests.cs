using GarageStack.Api.Services;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class VehicleFactsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    private static async Task<(AppDbContext Db, Vehicle Vehicle)> SetupAsync(CancellationToken ct, string? plate = "HPK98B")
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001", LicensePlate = plate };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        return (db, vehicle);
    }

    private static VehicleFactsService Service(AppDbContext db, bool rdwEnabled = true)
    {
        var telemetry = new TelemetryRepository(db);
        return new VehicleFactsService(
            new RdwRepository(db), new RdwOptions { Enabled = rdwEnabled }, new MaintenanceRepository(db),
            telemetry, new TripRepository(db, telemetry), new FixedTimeProvider(Now));
    }

    private static RdwVehicleInfo Lookup(int vehicleId, string plate = "HPK98B") => new()
    {
        VehicleId = vehicleId,
        LicensePlate = plate,
        Found = true,
        ApkExpiresOn = new DateOnly(2026, 11, 18),
        WamInsured = true,
        RecallsJson = RdwRecalls.Serialize(
        [
            new RdwRecall("MGP230061", "O", null, null, null, null, Now.UtcDateTime),
            new RdwRecall("MGP220011", "P", null, null, null, null, Now.UtcDateTime),
        ]),
    };

    [Fact]
    public async Task TheRdwLookup_GivesTheApkAndTheOpenRecalls()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        db.RdwVehicleInfos.Add(Lookup(vehicle.Id));
        await db.SaveChangesAsync(ct);

        var facts = await Service(db).GetAsync(vehicle, ct);

        Assert.Equal((new DateOnly(2026, 11, 18), 30, 1, true), (facts.ApkExpiresOn, facts.ApkDaysLeft, facts.OpenRecalls, facts.WamInsured));
        Assert.True(facts.HasRdw);
    }

    [Theory]
    [InlineData(false, "HPK98B")]
    [InlineData(true, "AB123C")]
    public async Task NoRdwFacts_WhileLookupsAreOff_OrForAnotherPlate(bool enabled, string lookedUpPlate)
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        db.RdwVehicleInfos.Add(Lookup(vehicle.Id, lookedUpPlate));
        await db.SaveChangesAsync(ct);

        var facts = await Service(db, rdwEnabled: enabled).GetAsync(vehicle, ct);

        Assert.False(facts.HasRdw);
    }

    [Fact]
    public async Task TheNextMaintenance_IsTheMostUrgentItem_ThenTheSoonest()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var now = Now.UtcDateTime;
        db.MaintenanceItems.AddRange(
            new MaintenanceItem { VehicleId = vehicle.Id, Name = "Not started", IntervalMonths = 12 },
            new MaintenanceItem { VehicleId = vehicle.Id, Name = "Tyres", IntervalMonths = 12, LastServiceDate = now.AddMonths(-2) },
            new MaintenanceItem { VehicleId = vehicle.Id, Name = "Oil", IntervalMonths = 12, LastServiceDate = now.AddMonths(-11).AddDays(-10) },
            new MaintenanceItem { VehicleId = vehicle.Id, Name = "Brake fluid", IntervalMonths = 24, LastServiceDate = now.AddMonths(-23) });
        await db.SaveChangesAsync(ct);

        var facts = await Service(db).GetAsync(vehicle, ct);

        Assert.Equal(("Oil", "dueSoon"), (facts.NextMaintenance, facts.NextMaintenanceStatus));
        Assert.True(facts.NextMaintenanceDaysLeft < 30);
    }

    [Fact]
    public async Task ACarWithNothingRecorded_HasNoFacts()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, vehicle) = await SetupAsync(ct, plate: null);
        await using var _ = db;

        var facts = await Service(db).GetAsync(vehicle, ct);

        Assert.False(facts.HasRdw);
        Assert.False(facts.HasMaintenance);
        Assert.False(facts.HasTrip);
    }
}
