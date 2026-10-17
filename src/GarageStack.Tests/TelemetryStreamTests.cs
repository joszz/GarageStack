using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

// The export streams every row a car ever sent. When the browser goes, the reading has to stop.
public class TelemetryStreamTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<int> SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        var start = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
        db.TelemetrySnapshots.AddRange(
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = start.AddMinutes(5), EvSocPercent = 49 },
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = start, EvSocPercent = 50 });
        await db.SaveChangesAsync(ct);
        return vehicle.Id;
    }

    [Fact]
    public async Task StreamAll_ReturnsEveryRowOldestFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb();
        var vehicleId = await SeedAsync(db, ct);

        var socs = new List<double?>();
        await foreach (var row in new TelemetryRepository(db).StreamAllAsync(vehicleId, ct))
            socs.Add(row.EvSocPercent);

        Assert.Equal(new double?[] { 50, 49 }, socs);
    }

    [Fact]
    public async Task StreamAll_StopsWhenTheCallerCancels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewDb();
        var vehicleId = await SeedAsync(db, ct);
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var row in new TelemetryRepository(db).StreamAllAsync(vehicleId, gone.Token))
                Assert.Fail($"read a row at {row.RecordedAt} after the caller had gone");
        });
    }
}
