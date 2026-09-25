using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class TripRepositoryTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private static async Task<(AppDbContext Db, TripRepository Repo, Vehicle Vehicle)> SetupAsync(CancellationToken ct)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        return (db, new TripRepository(db, new TelemetryRepository(db)), vehicle);
    }

    // A drive north from `start`, one fix every five minutes, then parked.
    private static TelemetrySnapshot[] Drive(int vehicleId, DateTime start) =>
    [
        new() { VehicleId = vehicleId, RecordedAt = start, Latitude = 51.50, Longitude = 0.0, Speed = 50 },
        new() { VehicleId = vehicleId, RecordedAt = start.AddMinutes(5), Latitude = 51.55, Longitude = 0.0, Speed = 50 },
        new() { VehicleId = vehicleId, RecordedAt = start.AddMinutes(10), Latitude = 51.60, Longitude = 0.0, Speed = 50 },
        new() { VehicleId = vehicleId, RecordedAt = start.AddMinutes(11), Latitude = 51.60, Longitude = 0.0, Speed = 0 },
    ];

    private static TripDto CutOne(TelemetrySnapshot[] drive) =>
        Assert.Single(TripSegmenter.Segment(
            [.. drive.Select(s => new TripPoint(s.RecordedAt, s.Latitude!.Value, s.Longitude!.Value, s.Speed))],
            DateTime.MaxValue).Trips);

    [Fact]
    public async Task SaveRecorded_SavesTheTripsAndMovesTheLine()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var trip = CutOne(Drive(vehicle.Id, T0));

        await repo.SaveRecordedAsync(vehicle.Id, [trip], T0.AddHours(1), ct);

        var saved = Assert.Single(await db.Trips.ToListAsync(ct));
        Assert.Equal(trip.StartedAt, saved.StartedAt);
        Assert.Equal(trip.EndedAt, saved.EndedAt);
        Assert.Equal(trip.DistanceKm, saved.DistanceKm);
        Assert.Equal(3, saved.PointCount);
        Assert.Equal(T0.AddHours(1), await repo.GetRecordedUntilAsync(vehicle.Id, ct));
    }

    [Fact]
    public async Task GetTrips_ServesSavedTripsThenTheOnesCutLive()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var savedDrive = Drive(vehicle.Id, T0);
        var liveDrive = Drive(vehicle.Id, T0.AddHours(2));
        db.TelemetrySnapshots.AddRange([.. savedDrive, .. liveDrive]);
        await db.SaveChangesAsync(ct);
        await repo.SaveRecordedAsync(vehicle.Id, [CutOne(savedDrive)], T0.AddHours(1), ct);

        var trips = await repo.GetTripsAsync(vehicle.Id, T0.AddDays(-1), T0.AddDays(1), ct);

        Assert.Equal(2, trips.Count);
        Assert.Equal([0, 1], trips.Select(t => t.Index));
        Assert.NotNull(trips[0].Id);
        Assert.Equal(T0, trips[0].StartedAt);
        Assert.Null(trips[1].Id);
        Assert.Equal(T0.AddHours(2), trips[1].StartedAt);
    }

    [Fact]
    public async Task GetTrips_SavedTripsKeepTheirFixesExactly()
    {
        // The map's snapped-line cache is keyed by the fixes a trip is drawn from, so a trip must
        // come back from the database with exactly the fixes it was cut with.
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var trip = CutOne(Drive(vehicle.Id, T0.AddTicks(1234567)));
        await repo.SaveRecordedAsync(vehicle.Id, [trip], T0.AddHours(1), ct);

        var served = Assert.Single(await repo.GetTripsAsync(vehicle.Id, T0.AddDays(-1), T0.AddDays(1), ct));

        Assert.Equal(trip.Points, served.Points);
    }

    [Fact]
    public async Task GetTrips_TripSavedPastTheLineItWasReadAt_IsServedOnce()
    {
        // The Worker saving a trip between the line being read and the saved trips being read looks
        // like a saved trip past the line: it must come from one source only, not both.
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var drive = Drive(vehicle.Id, T0.AddHours(2));
        db.TelemetrySnapshots.AddRange(drive);
        await db.SaveChangesAsync(ct);
        await repo.SaveRecordedAsync(vehicle.Id, [], T0.AddHours(1), ct);
        var trip = CutOne(drive);
        db.Trips.Add(new Trip
        {
            VehicleId = vehicle.Id,
            StartedAt = trip.StartedAt,
            EndedAt = trip.EndedAt,
            DistanceKm = trip.DistanceKm,
            PointCount = trip.PointCount,
            PointsJson = System.Text.Json.JsonSerializer.Serialize(trip.Points),
        });
        await db.SaveChangesAsync(ct);

        var trips = await repo.GetTripsAsync(vehicle.Id, T0.AddDays(-1), T0.AddDays(1), ct);

        Assert.Equal(T0.AddHours(2), Assert.Single(trips).StartedAt);
    }

    [Fact]
    public async Task GetTrips_NothingRecordedYet_CutsTheWholeWindowLive()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        db.TelemetrySnapshots.AddRange([.. Drive(vehicle.Id, T0), .. Drive(vehicle.Id, T0.AddHours(2))]);
        await db.SaveChangesAsync(ct);

        var trips = await repo.GetTripsAsync(vehicle.Id, T0.AddDays(-1), T0.AddDays(1), ct);

        Assert.Equal(2, trips.Count);
        Assert.All(trips, t => Assert.Null(t.Id));
    }

    [Fact]
    public async Task GetTrips_OnlyServesSavedTripsStartingInTheWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await repo.SaveRecordedAsync(vehicle.Id,
            [CutOne(Drive(vehicle.Id, T0)), CutOne(Drive(vehicle.Id, T0.AddDays(2)))],
            T0.AddDays(3), ct);

        var trips = await repo.GetTripsAsync(vehicle.Id, T0.AddDays(1), T0.AddDays(4), ct);

        Assert.Equal(T0.AddDays(2), Assert.Single(trips).StartedAt);
    }

    [Fact]
    public async Task GetTrips_SavedTripWithUnreadableFixes_IsLeftOut()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await repo.SaveRecordedAsync(vehicle.Id, [CutOne(Drive(vehicle.Id, T0))], T0.AddHours(1), ct);
        (await db.Trips.SingleAsync(ct)).PointsJson = "not json";
        await db.SaveChangesAsync(ct);

        Assert.Empty(await repo.GetTripsAsync(vehicle.Id, T0.AddDays(-1), T0.AddDays(1), ct));
    }
}
