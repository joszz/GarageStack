using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GarageStack.Tests;

public class VehicleRepositoryTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // ── GetByVinAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByVinAsync_ExistingVin_ReturnsVehicle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle { Vin = "VIN001" });
        await db.SaveChangesAsync(ct);

        var result = await new VehicleRepository(db).GetByVinAsync("VIN001", ct);

        Assert.NotNull(result);
        Assert.Equal("VIN001", result.Vin);
    }

    [Fact]
    public async Task GetByVinAsync_NonExistingVin_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        var result = await new VehicleRepository(db).GetByVinAsync("MISSING", ct);

        Assert.Null(result);
    }

    // ── GetOrCreateByVinAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetOrCreateByVinAsync_NewVin_CreatesAndReturnsVehicle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        var result = await new VehicleRepository(db).GetOrCreateByVinAsync("NEWVIN", null, ct);

        Assert.NotNull(result);
        Assert.Equal("NEWVIN", result.Vin);
        Assert.Equal(1, await db.Vehicles.CountAsync(ct));
    }

    [Fact]
    public async Task GetOrCreateByVinAsync_NewVin_WithSaicUser_SetsUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        var result = await new VehicleRepository(db).GetOrCreateByVinAsync("NEWVIN2", "alice@example.com", ct);

        Assert.Equal("alice@example.com", result.SaicUser);
    }

    [Fact]
    public async Task GetOrCreateByVinAsync_ExistingVin_ReturnsExistingWithoutDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle { Vin = "EXIST001" });
        await db.SaveChangesAsync(ct);

        var result = await new VehicleRepository(db).GetOrCreateByVinAsync("EXIST001", null, ct);

        Assert.Equal("EXIST001", result.Vin);
        Assert.Equal(1, await db.Vehicles.CountAsync(ct));
    }

    [Fact]
    public async Task GetOrCreateByVinAsync_ExistingVin_UpdatesSaicUserWhenChanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle { Vin = "EXIST002", SaicUser = "old@example.com" });
        await db.SaveChangesAsync(ct);

        var result = await new VehicleRepository(db).GetOrCreateByVinAsync("EXIST002", "new@example.com", ct);

        Assert.Equal("new@example.com", result.SaicUser);
    }

    [Fact]
    public async Task GetOrCreateByVinAsync_ExistingVin_NullSaicUser_DoesNotOverwriteExistingUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Vehicles.Add(new Vehicle { Vin = "EXIST003", SaicUser = "keep@example.com" });
        await db.SaveChangesAsync(ct);

        var result = await new VehicleRepository(db).GetOrCreateByVinAsync("EXIST003", null, ct);

        Assert.Equal("keep@example.com", result.SaicUser);
    }

    // ── SetModelAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SetModelAsync_ExistingVehicle_SetsModel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "MVIN001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        await new VehicleRepository(db).SetModelAsync(vehicle.Id, "MG ZS EV", ct);

        var updated = await db.Vehicles.FindAsync([vehicle.Id], ct);
        Assert.Equal("MG ZS EV", updated!.Model);
    }

    [Fact]
    public async Task SetModelAsync_SameModel_DoesNotChangeValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "MVIN002", Model = "MG ZS EV" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        // Should not throw or change anything
        await new VehicleRepository(db).SetModelAsync(vehicle.Id, "MG ZS EV", ct);

        var updated = await db.Vehicles.FindAsync([vehicle.Id], ct);
        Assert.Equal("MG ZS EV", updated!.Model);
    }

    [Fact]
    public async Task SetModelAsync_NonExistingVehicle_DoesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        // Should not throw
        await new VehicleRepository(db).SetModelAsync(9999, "SomeModel", ct);
    }

    // ── SetConfigValueAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SetConfigValueAsync_NewKey_AddsToConfig()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "CVIN001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        await new VehicleRepository(db).SetConfigValueAsync(vehicle.Id, "hw_version", "MG_BEV_1.0", ct);

        var updated = await db.Vehicles.FindAsync([vehicle.Id], ct);
        Assert.NotNull(updated!.ConfigJson);
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(updated.ConfigJson!);
        Assert.Equal("MG_BEV_1.0", config!["hw_version"]);
    }

    [Fact]
    public async Task SetConfigValueAsync_ExistingKey_UpdatesValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle
        {
            Vin = "CVIN002",
            ConfigJson = """{"hw_version":"OLD"}"""
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        await new VehicleRepository(db).SetConfigValueAsync(vehicle.Id, "hw_version", "NEW", ct);

        var updated = await db.Vehicles.FindAsync([vehicle.Id], ct);
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(updated!.ConfigJson!);
        Assert.Equal("NEW", config!["hw_version"]);
    }

    [Fact]
    public async Task SetConfigValueAsync_MultipleKeys_PreservesOtherKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle
        {
            Vin = "CVIN003",
            ConfigJson = """{"key1":"val1"}"""
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        await new VehicleRepository(db).SetConfigValueAsync(vehicle.Id, "key2", "val2", ct);

        var updated = await db.Vehicles.FindAsync([vehicle.Id], ct);
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(updated!.ConfigJson!);
        Assert.Equal("val1", config!["key1"]);
        Assert.Equal("val2", config["key2"]);
    }

    [Fact]
    public async Task SetConfigValueAsync_NonExistingVehicle_DoesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        // Should not throw
        await new VehicleRepository(db).SetConfigValueAsync(9999, "key", "value", ct);
    }
}
