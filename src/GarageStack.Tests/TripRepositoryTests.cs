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

    // ── Latest trip ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLatest_PrefersTheTripPastTheLine()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var savedDrive = Drive(vehicle.Id, T0);
        db.TelemetrySnapshots.AddRange([.. savedDrive, .. Drive(vehicle.Id, T0.AddHours(2))]);
        await db.SaveChangesAsync(ct);
        await repo.SaveRecordedAsync(vehicle.Id, [Recorded(CutOne(savedDrive))], T0.AddHours(1), ct);

        var latest = await repo.GetLatestAsync(vehicle.Id, T0.AddHours(3), ct);

        Assert.NotNull(latest);
        Assert.Null(latest.Id);
        Assert.Equal(T0.AddHours(2), latest.StartedAt);
        Assert.Equal(0, latest.Index);
    }

    [Fact]
    public async Task GetLatest_WithNothingPastTheLine_ServesTheNewestSavedTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await repo.SaveRecordedAsync(vehicle.Id,
            [Recorded(CutOne(Drive(vehicle.Id, T0))), Recorded(CutOne(Drive(vehicle.Id, T0.AddDays(1))))],
            T0.AddDays(1).AddHours(1), ct);

        // Weeks later: the saved trips are older than the lookback, and still the answer.
        var latest = await repo.GetLatestAsync(vehicle.Id, T0.AddDays(30), ct);

        Assert.NotNull(latest);
        Assert.NotNull(latest.Id);
        Assert.Equal(T0.AddDays(1), latest.StartedAt);
        Assert.Equal(3, latest.Points.Count);
    }

    [Fact]
    public async Task GetLatest_SkipsASavedTripWithUnreadableFixes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await repo.SaveRecordedAsync(vehicle.Id,
            [Recorded(CutOne(Drive(vehicle.Id, T0))), Recorded(CutOne(Drive(vehicle.Id, T0.AddDays(1))))],
            T0.AddDays(1).AddHours(1), ct);
        (await db.Trips.SingleAsync(t => t.StartedAt == T0.AddDays(1), ct)).PointsJson = "not json";
        await db.SaveChangesAsync(ct);

        var latest = await repo.GetLatestAsync(vehicle.Id, T0.AddDays(2), ct);

        Assert.Equal(T0, latest?.StartedAt);
    }

    [Fact]
    public async Task GetLatest_WithoutAnyTrip_IsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;

        Assert.Null(await repo.GetLatestAsync(vehicle.Id, T0, ct));
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

    // ── Commutes ──────────────────────────────────────────────────────────────

    // Drive() runs from 51.50 N to 51.60 N along the meridian.
    private static CommutePlace At(int vehicleId, CommutePlaceKind kind, double latitude) =>
        new() { VehicleId = vehicleId, Kind = kind, Latitude = latitude, Longitude = 0.0 };

    private static async Task<List<CommutePlace>> HomeAndWorkAsync(AppDbContext db, Vehicle vehicle, CancellationToken ct)
    {
        List<CommutePlace> places = [At(vehicle.Id, CommutePlaceKind.Home, 51.50), At(vehicle.Id, CommutePlaceKind.Work, 51.60)];
        db.CommutePlaces.AddRange(places);
        await db.SaveChangesAsync(ct);
        return places;
    }

    private static async Task<TripLogEntry> EntryAsync(TripRepository repo, Vehicle vehicle, long id, CancellationToken ct) =>
        Assert.Single(await repo.GetLogEntriesAsync(vehicle.Id, [id], ct));

    [Fact]
    public async Task SaveRecorded_TripBetweenHomeAndWork_IsSavedAsAnAutomaticCommute()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await HomeAndWorkAsync(db, vehicle, ct);

        var entry = await EntryAsync(repo, vehicle, await SaveOneAsync(repo, vehicle, T0, ct), ct);

        Assert.Equal(TripPurpose.Commute, entry.Purpose);
        Assert.True(entry.PurposeIsAutomatic);
    }

    [Fact]
    public async Task SaveRecorded_WithoutHomeAndWork_LeavesThePurposeOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;

        var entry = await EntryAsync(repo, vehicle, await SaveOneAsync(repo, vehicle, T0, ct), ct);

        Assert.Null(entry.Purpose);
        Assert.False(entry.PurposeIsAutomatic);
    }

    // Setting home and work for the first time labels the history too, but never a trip someone
    // classified, not even one deliberately left unclassified.
    [Fact]
    public async Task RelabelCommutes_MarksTripsNobodyChoseFor_AndLeavesChosenOnesAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var open = await SaveOneAsync(repo, vehicle, T0, ct);
        var chosen = await SaveOneAsync(repo, vehicle, T0.AddDays(1), ct);
        var leftOpen = await SaveOneAsync(repo, vehicle, T0.AddDays(2), ct);
        await repo.SetPurposeAndNotesAsync(vehicle.Id, chosen, TripPurpose.Private, null, ct);
        await repo.SetPurposeAsync(vehicle.Id, [leftOpen], null, ct);

        var relabel = await repo.RelabelCommutesAsync(vehicle.Id, await HomeAndWorkAsync(db, vehicle, ct), ct);

        Assert.Equal(new CommuteRelabel(Marked: 1, Cleared: 0), relabel);
        Assert.Equal(TripPurpose.Commute, (await EntryAsync(repo, vehicle, open, ct)).Purpose);
        Assert.Equal(TripPurpose.Private, (await EntryAsync(repo, vehicle, chosen, ct)).Purpose);
        Assert.Null((await EntryAsync(repo, vehicle, leftOpen, ct)).Purpose);
    }

    [Fact]
    public async Task RelabelCommutes_WorkMovedAway_UnmarksWhatItHadMarked()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await HomeAndWorkAsync(db, vehicle, ct);
        var id = await SaveOneAsync(repo, vehicle, T0, ct);

        var relabel = await repo.RelabelCommutesAsync(vehicle.Id,
            [At(vehicle.Id, CommutePlaceKind.Home, 51.50), At(vehicle.Id, CommutePlaceKind.Work, 51.90)], ct);

        Assert.Equal(new CommuteRelabel(Marked: 0, Cleared: 1), relabel);
        var entry = await EntryAsync(repo, vehicle, id, ct);
        Assert.Null(entry.Purpose);
        Assert.False(entry.PurposeIsAutomatic);
    }

    // The trip log sends the purpose back with every notes edit; that is no choice about it.
    [Fact]
    public async Task SetPurposeAndNotes_OnlyNotesEdited_KeepsTheLabelAutomatic()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await HomeAndWorkAsync(db, vehicle, ct);
        var id = await SaveOneAsync(repo, vehicle, T0, ct);

        var entry = await repo.SetPurposeAndNotesAsync(vehicle.Id, id, TripPurpose.Commute, "Early start", ct);

        Assert.True(entry!.PurposeIsAutomatic);
        Assert.Equal("Early start", entry.Notes);
    }

    [Fact]
    public async Task SetPurposeAndNotes_PurposeChanged_IsNoLongerRelabelled()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        var places = await HomeAndWorkAsync(db, vehicle, ct);
        var id = await SaveOneAsync(repo, vehicle, T0, ct);

        var entry = await repo.SetPurposeAndNotesAsync(vehicle.Id, id, TripPurpose.Business, null, ct);
        var relabel = await repo.RelabelCommutesAsync(vehicle.Id, places, ct);

        Assert.False(entry!.PurposeIsAutomatic);
        Assert.Equal(new CommuteRelabel(0, 0), relabel);
        Assert.Equal(TripPurpose.Business, (await EntryAsync(repo, vehicle, id, ct)).Purpose);
    }

    // ── Trip summaries ────────────────────────────────────────────────────────

    // Drive()'s trip is its three fixes at 50 km/h; the parked fix after them is not part of it.
    [Fact]
    public async Task SaveRecorded_StoresTheSpeedFiguresWithTheTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;

        await SaveOneAsync(repo, vehicle, T0, ct);

        var saved = await db.Trips.SingleAsync(ct);
        Assert.Equal(50, saved.MaxSpeedKmh);
        Assert.Equal(50, saved.AvgMovingSpeedKmh);
        Assert.Equal(3, saved.MovingSpeedSamples);
    }

    // A year of trips must not read a year of fixes: the stored figures are enough.
    [Fact]
    public async Task GetSummaries_ServesStoredFigures_WithoutReadingTheFixes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await SaveOneAsync(repo, vehicle, T0, ct);
        (await db.Trips.SingleAsync(ct)).PointsJson = "not json";
        await db.SaveChangesAsync(ct);

        var summary = Assert.Single(await repo.GetSummariesAsync(vehicle.Id, T0.AddDays(-1), T0.AddDays(1), ct));

        Assert.Equal(50, summary.MaxSpeedKmh);
        Assert.Equal(3, summary.MovingSpeedSamples);
        Assert.Equal(51.60, summary.EndLatitude);
    }

    // A trip saved before the figures were stored, which housekeeping has not reached yet.
    [Fact]
    public async Task GetSummaries_TripWithoutStoredFigures_IsReadOffItsFixes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await SaveOneAsync(repo, vehicle, T0, ct);
        var trip = await db.Trips.SingleAsync(ct);
        (trip.MaxSpeedKmh, trip.AvgMovingSpeedKmh, trip.MovingSpeedSamples) = (null, null, null);
        await db.SaveChangesAsync(ct);

        var summary = Assert.Single(await repo.GetSummariesAsync(vehicle.Id, T0.AddDays(-1), T0.AddDays(1), ct));

        Assert.Equal(50, summary.MaxSpeedKmh);
        Assert.Equal(3, summary.MovingSpeedSamples);
    }

    [Fact]
    public async Task GetSummaries_MatchTheTripsTheyStandFor()
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
        var summaries = await repo.GetSummariesAsync(vehicle.Id, T0.AddDays(-1), T0.AddDays(1), ct);

        Assert.Equal(trips.Select(t => t.ToSummary()), summaries);
    }

    [Fact]
    public async Task FillSpeedFigures_WorksOutOlderTrips_InBatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicle) = await SetupAsync(ct);
        await using var _ = db;
        await SaveOneAsync(repo, vehicle, T0, ct);
        await SaveOneAsync(repo, vehicle, T0.AddDays(1), ct);
        await SaveOneAsync(repo, vehicle, T0.AddDays(2), ct);
        foreach (var t in await db.Trips.ToListAsync(ct))
            (t.MaxSpeedKmh, t.AvgMovingSpeedKmh, t.MovingSpeedSamples) = (null, null, null);
        (await db.Trips.OrderBy(t => t.Id).LastAsync(ct)).PointsJson = "not json";
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        Assert.Equal(2, await repo.FillSpeedFiguresAsync(2, ct));
        Assert.Equal(1, await repo.FillSpeedFiguresAsync(2, ct));
        Assert.Equal(0, await repo.FillSpeedFiguresAsync(2, ct));

        var trips = await db.Trips.AsNoTracking().OrderBy(t => t.Id).ToListAsync(ct);
        Assert.Equal([3, 3, 0], trips.Select(t => t.MovingSpeedSamples));
        Assert.Equal([50, 50, null], trips.Select(t => t.MaxSpeedKmh));
    }
}
