using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

// What only PostgreSQL decides about trips: the unique index that turns a slip in the recording
// line into an error, the transaction around saving and joining, and the SQL the filters become.
public class TripRepositoryPostgresTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);

    private static async Task<int> AddVehicleAsync(AppDbContext db, CancellationToken ct)
    {
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        return vehicle.Id;
    }

    private static TripRepository Repository(AppDbContext db) => new(db, new TelemetryRepository(db));

    // A drive north from one latitude to another, a fix every five minutes.
    private static RecordedTrip Leg(DateTime start, double from, double to, double? odometerStart = null, double? odometerEnd = null)
    {
        TripPoint[] fixes =
        [
            new(start, from, 5.0, 40),
            new(start.AddMinutes(5), (from + to) / 2, 5.0, 90),
            new(start.AddMinutes(10), to, 5.0, 50),
        ];
        return new RecordedTrip(
            new TripDto(0, start, start.AddMinutes(10), GeoHelper.Haversine(from, 5.0, to, 5.0), fixes.Length, fixes),
            odometerStart, odometerEnd);
    }

    [Fact]
    public async Task ATripSavedTwice_IsRefused_AndTheLineDoesNotMove()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);
        await Repository(db).SaveRecordedAsync(vehicleId, [Leg(T0, 52.0, 52.1)], T0.AddHours(1), ct);

        await using var again = database.CreateContext();
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            Repository(again).SaveRecordedAsync(vehicleId, [Leg(T0, 52.0, 52.1)], T0.AddHours(2), ct));

        await using var check = database.CreateContext();
        Assert.Equal(1, await check.Trips.CountAsync(ct));
        Assert.Equal(T0.AddHours(1), await Repository(check).GetRecordedUntilAsync(vehicleId, ct));
    }

    [Fact]
    public async Task HiddenTrips_AreLeftOutByTheQueries_AndListedInTheLog()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);
        var repo = Repository(db);
        await repo.SaveRecordedAsync(vehicleId, [Leg(T0, 52.0, 52.1), Leg(T0.AddHours(2), 52.1, 52.2)], T0.AddHours(3), ct);
        var ids = await db.Trips.OrderBy(t => t.StartedAt).Select(t => t.Id).ToListAsync(ct);

        await repo.SetHiddenAsync(vehicleId, ids[1], hidden: true, ct);

        await using var check = database.CreateContext();
        var read = Repository(check);
        Assert.Equal([ids[0]], (await read.GetTripsAsync(vehicleId, T0.AddDays(-1), T0.AddDays(1), ct)).Select(t => t.Id));
        Assert.Equal([ids[0]], (await read.GetSummariesAsync(vehicleId, T0.AddDays(-1), T0.AddDays(1), ct)).Select(t => t.Id));
        Assert.Equal(ids[0], (await read.GetLatestAsync(vehicleId, T0.AddHours(4), ct))!.Id);
        Assert.Equal([false, true], (await read.GetLogAsync(vehicleId, T0.AddDays(-1), T0.AddDays(1), ct)).Select(e => e.IsHidden));
    }

    [Fact]
    public async Task Joining_ReplacesTwoRowsWithOne_UnderTheFirstOnesId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);
        db.CommutePlaces.AddRange(
            new CommutePlace { VehicleId = vehicleId, Kind = CommutePlaceKind.Home, Latitude = 52.0, Longitude = 5.0 },
            new CommutePlace { VehicleId = vehicleId, Kind = CommutePlaceKind.Work, Latitude = 52.2, Longitude = 5.0 });
        await db.SaveChangesAsync(ct);
        await Repository(db).SaveRecordedAsync(vehicleId,
            [Leg(T0, 52.0, 52.1, 1000, 1011), Leg(T0.AddMinutes(20), 52.1, 52.2, 1011, 1022)], T0.AddHours(1), ct);
        var ids = await db.Trips.OrderBy(t => t.StartedAt).Select(t => t.Id).ToListAsync(ct);

        await using var joining = database.CreateContext();
        var result = await Repository(joining).MergeWithNextAsync(vehicleId, ids[0], ct);

        await using var check = database.CreateContext();
        var trip = Assert.Single(await check.Trips.ToListAsync(ct));
        Assert.Equal(TripMergeOutcome.Merged, result.Outcome);
        Assert.Equal(ids[0], trip.Id);
        Assert.Equal((T0, T0.AddMinutes(30), 6), (trip.StartedAt, trip.EndedAt, trip.PointCount));
        Assert.Equal((1000.0, 1022.0), (trip.OdometerStartKm, trip.OdometerEndKm));
        Assert.Equal(TripPurpose.Commute, trip.Purpose);
        Assert.Equal(6, Assert.IsType<List<TripPoint>>(SafeJson.TryDeserialize<List<TripPoint>>(trip.PointsJson)).Count);
    }

    [Fact]
    public async Task Relabelling_MarksTheTripsBetweenHomeAndWork()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);
        var repo = Repository(db);
        await repo.SaveRecordedAsync(vehicleId, [Leg(T0, 52.0, 52.2), Leg(T0.AddHours(2), 52.5, 52.6)], T0.AddHours(3), ct);
        CommutePlace[] places =
        [
            new() { VehicleId = vehicleId, Kind = CommutePlaceKind.Home, Latitude = 52.0, Longitude = 5.0 },
            new() { VehicleId = vehicleId, Kind = CommutePlaceKind.Work, Latitude = 52.2, Longitude = 5.0 },
        ];

        var relabel = await repo.RelabelCommutesAsync(vehicleId, places, ct);

        Assert.Equal(new CommuteRelabel(1, 0), relabel);
        await using var check = database.CreateContext();
        Assert.Equal([TripPurpose.Commute, null], await check.Trips.OrderBy(t => t.StartedAt).Select(t => t.Purpose).ToListAsync(ct));
    }

    // Editing a route as PostgreSQL stores it: the neighbours and the recording line bound it, the
    // log reads the edit off EditedAt, and a join and a reset keep the points as recorded.
    [Fact]
    public async Task EditingARoute_IsKeptWithTheRecordedPoints_ThroughAJoinAndAReset()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);
        await Repository(db).SaveRecordedAsync(vehicleId,
            [Leg(T0, 52.0, 52.1, 1000, 1011), Leg(T0.AddMinutes(20), 52.1, 52.2, 1011, 1022)], T0.AddHours(1), ct);
        var ids = await db.Trips.OrderBy(t => t.StartedAt).Select(t => t.Id).ToListAsync(ct);

        await using var editing = database.CreateContext();
        var repo = Repository(editing);
        var editor = (await repo.GetEditorAsync(vehicleId, ids[1], ct)).Editor!;
        var saved = await repo.SetPointsAsync(vehicleId, ids[1],
            [new(new DateTimeOffset(T0.AddMinutes(18)), 52.09, 5.0),
             .. editor.Points.Select(p => new TripPointInput(new DateTimeOffset(p.RecordedAt), p.Latitude, p.Longitude))], ct);
        var log = await repo.GetLogAsync(vehicleId, T0, T0.AddDays(1), ct);

        Assert.Equal((T0.AddMinutes(10), T0.AddHours(1)), (editor.NotBefore, editor.NotAfter));
        Assert.Equal(TripEditOutcome.Ok, saved.Outcome);
        Assert.Equal([false, true], log.Select(e => e.IsEdited));
        Assert.Equal(T0.AddMinutes(18), log[1].StartedAt);

        await using var joining = database.CreateContext();
        await Repository(joining).MergeWithNextAsync(vehicleId, ids[0], ct);
        await using var resetting = database.CreateContext();
        await Repository(resetting).ResetPointsAsync(vehicleId, ids[0], ct);

        await using var check = database.CreateContext();
        var trip = Assert.Single(await check.Trips.ToListAsync(ct));
        Assert.Equal((T0, 6), (trip.StartedAt, trip.PointCount));
        Assert.Null(trip.OriginalPointsJson);
        Assert.Null(trip.EditedAt);
    }

    [Fact]
    public async Task AnEdit_IsRefused_WhenTheTripChangesUnderIt_AndAPurposeIsNot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);
        await Repository(db).SaveRecordedAsync(vehicleId, [Leg(T0, 52.0, 52.1)], T0.AddHours(1), ct);
        var id = await db.Trips.Select(t => t.Id).SingleAsync(ct);

        // The Worker's phone fusion, holding the trip's row until it commits.
        await using var fusion = database.CreateContext();
        await using var fusing = await fusion.Database.BeginTransactionAsync(ct);
        await fusion.Trips.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.PhoneFusion, PhoneFusionOutcome.Fused), ct);

        await using var editing = database.CreateContext();
        var edit = Repository(editing).SetPointsAsync(vehicleId, id,
            [new(new DateTimeOffset(T0), 52.0, 5.0), new(new DateTimeOffset(T0.AddMinutes(10)), 52.1, 5.0)], ct);
        // The edit has read the trip and now waits on the row; only then does the fusion commit.
        // Watched from a connection of its own: inside a transaction pg_stat_activity stands still.
        await using var watch = database.CreateContext();
        while (!edit.IsCompleted && await watch.Database
                   .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()")
                   .SingleAsync(ct) == 0)
            await Task.Delay(20, ct);
        await fusing.CommitAsync(ct);
        var refused = await edit;

        await using var purposes = database.CreateContext();
        var labelled = await Repository(purposes).SetPurposeAndNotesAsync(vehicleId, id, TripPurpose.Business, null, ct);

        await using var check = database.CreateContext();
        var trip = await check.Trips.SingleAsync(ct);
        Assert.Equal(TripEditOutcome.Conflict, refused.Outcome);
        Assert.Equal(PhoneFusionOutcome.Fused, trip.PhoneFusion);
        Assert.Null(trip.EditedAt);
        Assert.NotNull(labelled);
        Assert.Equal(TripPurpose.Business, trip.Purpose);
    }
}
