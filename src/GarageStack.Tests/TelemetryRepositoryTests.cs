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

    // ── Trip detection tests ─────────────────────────────────────────────────

    private static TelemetrySnapshot MakePoint(int vehicleId, DateTime at, double lat, double lon, double? speed) =>
        new() { VehicleId = vehicleId, RecordedAt = at, Latitude = lat, Longitude = lon, Speed = speed };

    [Fact]
    public async Task GetTrips_BackToBackWithParkStop_ReturnsTwoTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "TRIP00000000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        var t0 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        db.TelemetrySnapshots.AddRange(
            // Trip 1: A -> B
            MakePoint(vehicle.Id, t0.AddMinutes(0),  51.50, 0.0, 50),
            MakePoint(vehicle.Id, t0.AddMinutes(5),  51.55, 0.0, 50),
            MakePoint(vehicle.Id, t0.AddMinutes(10), 51.60, 0.0, 50),
            // Parked at B for 10 minutes (> 5 min threshold)
            MakePoint(vehicle.Id, t0.AddMinutes(11), 51.60, 0.0, 0),
            MakePoint(vehicle.Id, t0.AddMinutes(15), 51.60, 0.0, 0),
            MakePoint(vehicle.Id, t0.AddMinutes(20), 51.60, 0.0, 0),
            // Trip 2: B -> A
            MakePoint(vehicle.Id, t0.AddMinutes(21), 51.60, 0.0, 50),
            MakePoint(vehicle.Id, t0.AddMinutes(25), 51.55, 0.0, 50),
            MakePoint(vehicle.Id, t0.AddMinutes(30), 51.50, 0.0, 50)
        );
        await db.SaveChangesAsync(ct);

        var result = await new TelemetryRepository(db).GetTripsAsync(vehicle.Id, t0.AddHours(-1), t0.AddHours(1), ct);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetTrips_BriefTrafficStop_DoesNotSplitTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "TRIP00000000000002" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        var t0 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        db.TelemetrySnapshots.AddRange(
            MakePoint(vehicle.Id, t0.AddMinutes(0),  51.50, 0.0, 50),
            MakePoint(vehicle.Id, t0.AddMinutes(5),  51.55, 0.0, 50),
            // Brief stop at a traffic light (2 minutes < 5 min threshold)
            MakePoint(vehicle.Id, t0.AddMinutes(6),  51.55, 0.0, 0),
            MakePoint(vehicle.Id, t0.AddMinutes(7),  51.55, 0.0, 0),
            MakePoint(vehicle.Id, t0.AddMinutes(8),  51.55, 0.0, 50),
            MakePoint(vehicle.Id, t0.AddMinutes(13), 51.60, 0.0, 50)
        );
        await db.SaveChangesAsync(ct);

        var result = await new TelemetryRepository(db).GetTripsAsync(vehicle.Id, t0.AddHours(-1), t0.AddHours(1), ct);

        Assert.Single(result);
    }

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
    public async Task GetMergedLatest_SnapshotWithOnlyIsAvailable_IsNotFilteredOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "TEST00000000000003" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        db.TelemetrySnapshots.Add(new TelemetrySnapshot
        {
            VehicleId = vehicle.Id,
            RecordedAt = DateTime.UtcNow.AddMinutes(-5),
            IsAvailable = true,
        });
        await db.SaveChangesAsync(ct);

        var result = await new TelemetryRepository(db).GetMergedLatestAsync(vehicle.Id, ct);

        Assert.NotNull(result);
        Assert.True(result.IsAvailable);
    }

    [Fact]
    public async Task GetMergedLatest_NewerFieldsInOlderRows_AreMergedIntoResult()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "TEST00000000000004" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        var now = DateTime.UtcNow;
        db.TelemetrySnapshots.Add(new TelemetrySnapshot
        {
            VehicleId = vehicle.Id,
            RecordedAt = now.AddMinutes(-1),
            EvSocPercent = 80,
        });
        db.TelemetrySnapshots.Add(new TelemetrySnapshot
        {
            VehicleId = vehicle.Id,
            RecordedAt = now.AddMinutes(-5),
            CurrentJourneyDistance = 12.5,
            ChargingType = "AC",
            BatteryHeating = true,
            Elevation = 55.0,
            ObcCurrent = 16.0,
        });
        await db.SaveChangesAsync(ct);

        var result = await new TelemetryRepository(db).GetMergedLatestAsync(vehicle.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(80, result.EvSocPercent);
        Assert.Equal(12.5, result.CurrentJourneyDistance);
        Assert.Equal("AC", result.ChargingType);
        Assert.True(result.BatteryHeating);
        Assert.Equal(55.0, result.Elevation);
        Assert.Equal(16.0, result.ObcCurrent);
    }

    [Fact]
    public async Task GetMergedLatest_JourneyDistanceCleared_WhenStationaryOverFiveMinutes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "TEST00000000000005" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        var now = DateTime.UtcNow;
        db.TelemetrySnapshots.AddRange(
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = now.AddMinutes(-1), EvSocPercent = 80 },
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = now.AddMinutes(-6), Speed = 0, CurrentJourneyDistance = 15.2 }
        );
        await db.SaveChangesAsync(ct);

        var result = await new TelemetryRepository(db).GetMergedLatestAsync(vehicle.Id, ct);

        Assert.NotNull(result);
        Assert.Null(result.CurrentJourneyDistance);
    }

    [Fact]
    public async Task GetMergedLatest_JourneyDistanceCleared_WhenEngineOffOverFiveMinutes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "TEST00000000000007" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        var now = DateTime.UtcNow;
        db.TelemetrySnapshots.AddRange(
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = now.AddMinutes(-1), EngineRunning = false },
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = now.AddMinutes(-2), CurrentJourneyDistance = 15.2 }
        );
        await db.SaveChangesAsync(ct);

        var result = await new TelemetryRepository(db).GetMergedLatestAsync(vehicle.Id, ct);

        Assert.NotNull(result);
        Assert.Null(result.CurrentJourneyDistance);
    }

    [Fact]
    public async Task GetMergedLatest_JourneyDistanceKept_WhenRecentlyMoving()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var vehicle = new Vehicle { Vin = "TEST00000000000006" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        var now = DateTime.UtcNow;
        db.TelemetrySnapshots.AddRange(
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = now.AddMinutes(-1), Speed = 60, CurrentJourneyDistance = 8.3 }
        );
        await db.SaveChangesAsync(ct);

        var result = await new TelemetryRepository(db).GetMergedLatestAsync(vehicle.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(8.3, result.CurrentJourneyDistance);
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
