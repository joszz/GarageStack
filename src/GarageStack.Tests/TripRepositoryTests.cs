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

    private static RecordedTrip Recorded(TripDto trip, double? odometerStartKm = null, double? odometerEndKm = null) =>
        new(trip, odometerStartKm, odometerEndKm);

    [Fact]
    public async Task SaveRecorded_SavesTheTripsAndMovesTheLine()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var trip = CutOne(Drive(vehicle.Id, T0));

        await repo.SaveRecordedAsync(vehicle.Id, [Recorded(trip)], T0.AddHours(1), ct);

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
        await repo.SaveRecordedAsync(vehicle.Id, [Recorded(CutOne(savedDrive))], T0.AddHours(1), ct);

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
        await repo.SaveRecordedAsync(vehicle.Id, [Recorded(trip)], T0.AddHours(1), ct);

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
            [Recorded(CutOne(Drive(vehicle.Id, T0))), Recorded(CutOne(Drive(vehicle.Id, T0.AddDays(2))))],
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
        await repo.SaveRecordedAsync(vehicle.Id, [Recorded(CutOne(Drive(vehicle.Id, T0)))], T0.AddHours(1), ct);
        (await db.Trips.SingleAsync(ct)).PointsJson = "not json";
        await db.SaveChangesAsync(ct);

        Assert.Empty(await repo.GetTripsAsync(vehicle.Id, T0.AddDays(-1), T0.AddDays(1), ct));
    }

    // ── Trip log ─────────────────────────────────────────────────────────────

    private static readonly PlaceAddress Home = new("Grote Markt 1, Zwolle", "Grote Markt", "1", "Zwolle", "8011 LW", "nl");
    private static readonly PlaceAddress Office = new("Brink 2, Deventer", "Brink", "2", "Deventer", "7411 BT", "nl");

    private static async Task<long> SaveOneAsync(TripRepository repo, Vehicle vehicle, DateTime start, CancellationToken ct,
        double? odometerStartKm = null, double? odometerEndKm = null)
    {
        await repo.SaveRecordedAsync(vehicle.Id,
            [Recorded(CutOne(Drive(vehicle.Id, start)), odometerStartKm, odometerEndKm)],
            start.AddHours(1), ct);
        var log = await repo.GetLogAsync(vehicle.Id, start, start.AddMinutes(1), ct);
        return Assert.Single(log).Id;
    }

    [Fact]
    public async Task GetLog_ServesSavedTripsWithTheirEndsAndOdometer_WithoutFixes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await SaveOneAsync(repo, vehicle, T0, ct, odometerStartKm: 24000.1, odometerEndKm: 24012.3);

        var entry = Assert.Single(await repo.GetLogAsync(vehicle.Id, T0.AddDays(-1), T0.AddDays(1), ct));

        Assert.Equal(T0, entry.StartedAt);
        Assert.Equal((51.50, 0.0), (entry.StartLatitude, entry.StartLongitude));
        Assert.Equal((51.60, 0.0), (entry.EndLatitude, entry.EndLongitude));
        Assert.Equal(24000.1, entry.OdometerStartKm);
        Assert.Equal(24012.3, entry.OdometerEndKm);
        Assert.Null(entry.StartPlace);
        Assert.Null(entry.Purpose);
        Assert.Null(entry.Notes);
    }

    [Fact]
    public async Task GetLog_IsOldestFirst_AndLeavesOutTripsOutsideTheWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await SaveOneAsync(repo, vehicle, T0.AddDays(2), ct);
        await SaveOneAsync(repo, vehicle, T0, ct);
        await SaveOneAsync(repo, vehicle, T0.AddDays(5), ct);

        var log = await repo.GetLogAsync(vehicle.Id, T0, T0.AddDays(5), ct);

        Assert.Equal([T0, T0.AddDays(2)], log.Select(e => e.StartedAt));
    }

    [Fact]
    public async Task GetLog_OnlyServesTheVehiclesOwnTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var other = new Vehicle { Vin = "FAKEVN00000000002" };
        db.Vehicles.Add(other);
        await db.SaveChangesAsync(ct);
        var id = await SaveOneAsync(repo, vehicle, T0, ct);

        Assert.Empty(await repo.GetLogAsync(other.Id, T0.AddDays(-1), T0.AddDays(1), ct));
        Assert.Empty(await repo.GetLogEntriesAsync(other.Id, [id], ct));
        Assert.Null(await repo.SetPurposeAndNotesAsync(other.Id, id, TripPurpose.Private, "not mine", ct));
        Assert.Equal(0, await repo.SetPurposeAsync(other.Id, [id], TripPurpose.Private, ct));

        var entry = Assert.Single(await repo.GetLogEntriesAsync(vehicle.Id, [id], ct));
        Assert.Null(entry.Purpose);
        Assert.Null(entry.Notes);
    }

    [Fact]
    public async Task SetPurposeAndNotes_ReplacesBoth()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var id = await SaveOneAsync(repo, vehicle, T0, ct);

        var first = await repo.SetPurposeAndNotesAsync(vehicle.Id, id, TripPurpose.Business, "Client visit", ct);
        var cleared = await repo.SetPurposeAndNotesAsync(vehicle.Id, id, null, null, ct);

        Assert.Equal((TripPurpose.Business, "Client visit"), (first!.Purpose, first.Notes));
        Assert.Equal((null, null), (cleared!.Purpose, cleared.Notes));
    }

    [Fact]
    public async Task SetPurpose_ChangesEveryListedTrip_AndKeepsTheirNotes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var first = await SaveOneAsync(repo, vehicle, T0, ct);
        var second = await SaveOneAsync(repo, vehicle, T0.AddDays(1), ct);
        var untouched = await SaveOneAsync(repo, vehicle, T0.AddDays(2), ct);
        await repo.SetPurposeAndNotesAsync(vehicle.Id, first, null, "Keep me", ct);

        Assert.Equal(2, await repo.SetPurposeAsync(vehicle.Id, [first, second], TripPurpose.Commute, ct));

        var log = (await repo.GetLogAsync(vehicle.Id, T0, T0.AddDays(3), ct)).ToDictionary(e => e.Id);
        Assert.Equal(TripPurpose.Commute, log[first].Purpose);
        Assert.Equal("Keep me", log[first].Notes);
        Assert.Equal(TripPurpose.Commute, log[second].Purpose);
        Assert.Null(log[untouched].Purpose);
    }

    [Fact]
    public async Task SavePlaces_KeepsThemOnTheTrip_AndANullLeavesTheStoredOneAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var id = await SaveOneAsync(repo, vehicle, T0, ct);

        await repo.SavePlacesAsync(vehicle.Id, [new TripPlaces(id, Home, null)], ct);
        await repo.SavePlacesAsync(vehicle.Id, [new TripPlaces(id, null, Office)], ct);

        var entry = Assert.Single(await repo.GetLogEntriesAsync(vehicle.Id, [id], ct));
        Assert.Equal(Home, entry.StartPlace);
        Assert.Equal(Office, entry.EndPlace);
    }

    [Fact]
    public async Task GetLog_UnreadablePlace_IsServedAsNotLookedUpYet()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var id = await SaveOneAsync(repo, vehicle, T0, ct);
        await repo.SavePlacesAsync(vehicle.Id, [new TripPlaces(id, Home, Office)], ct);
        (await db.Trips.SingleAsync(ct)).StartPlaceJson = "not json";
        await db.SaveChangesAsync(ct);

        var entry = Assert.Single(await repo.GetLogEntriesAsync(vehicle.Id, [id], ct));

        Assert.Null(entry.StartPlace);
        Assert.Equal(Office, entry.EndPlace);
    }
}
