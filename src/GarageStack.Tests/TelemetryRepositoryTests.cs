using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class TelemetryRepositoryTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // ── RemoteTemperature regression tests ──────────────────────────────────
    // These cover the two-part bug: (1) HasData must not filter out rows whose
    // only non-null field is RemoteTemperature, and (2) the merge loop must
    // copy RemoteTemperature into the merged snapshot.

    [Fact]
    public async Task GetMergedLatest_SnapshotWithOnlyRemoteTemperature_IsNotFilteredOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "TEST00000000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        db.TelemetrySnapshots.Add(new TelemetrySnapshot
        {
            VehicleId = vehicle.Id,
            RecordedAt = DateTime.UtcNow.AddMinutes(-5),
            RemoteTemperature = 21.5,
        });
        await db.SaveChangesAsync(ct);

        var result = await new TelemetryRepository(db).GetMergedLatestAsync(vehicle.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(21.5, result.RemoteTemperature);
    }

    [Fact]
    public async Task GetMergedLatest_RemoteTemperatureInOlderRow_IsMergedIntoResult()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "TEST00000000000002" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        var now = DateTime.UtcNow;
        // Newer row has fuel level but no remote temperature.
        db.TelemetrySnapshots.Add(new TelemetrySnapshot
        {
            VehicleId = vehicle.Id,
            RecordedAt = now.AddMinutes(-1),
            FuelLevelPercent = 80,
        });
        // Older row has remote temperature but nothing else.
        db.TelemetrySnapshots.Add(new TelemetrySnapshot
        {
            VehicleId = vehicle.Id,
            RecordedAt = now.AddMinutes(-10),
            RemoteTemperature = 22.0,
        });
        await db.SaveChangesAsync(ct);

        var result = await new TelemetryRepository(db).GetMergedLatestAsync(vehicle.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(80, result.FuelLevelPercent);   // from newer row
        Assert.Equal(22.0, result.RemoteTemperature); // merged from older row
    }
}
