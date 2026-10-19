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
}
