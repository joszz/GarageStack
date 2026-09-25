using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class TripRecorderTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private sealed class Setup : IAsyncDisposable
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        public Vehicle Vehicle { get; } = new() { Vin = "FAKEVN00000000001" };
        public TripRepository Trips { get; }
        public TripRecorder Recorder { get; }

        public Setup()
        {
            var telemetry = new TelemetryRepository(Db);
            Trips = new TripRepository(Db, telemetry);
            Recorder = new TripRecorder(telemetry, Trips, NullLogger.Instance);
            Db.Vehicles.Add(Vehicle);
            Db.SaveChanges();
        }

        public async Task AddFixesAsync(IEnumerable<(DateTime At, double Lat, double? Speed)> fixes, CancellationToken ct)
        {
            Db.TelemetrySnapshots.AddRange(fixes.Select(f => new TelemetrySnapshot
            {
                VehicleId = Vehicle.Id,
                RecordedAt = f.At,
                Latitude = f.Lat,
                Longitude = 0.0,
                Speed = f.Speed,
            }));
            await Db.SaveChangesAsync(ct);
        }

        public async Task AddOdometerAsync(DateTime at, double km, CancellationToken ct)
        {
            Db.TelemetrySnapshots.Add(new TelemetrySnapshot { VehicleId = Vehicle.Id, RecordedAt = at, OdometerKm = km });
            await Db.SaveChangesAsync(ct);
        }

        public Task<List<Trip>> SavedAsync(CancellationToken ct) =>
            Db.Trips.AsNoTracking().OrderBy(t => t.StartedAt).ToListAsync(ct);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    // A drive north from `start`, one fix every five minutes, then parked.
    private static (DateTime, double, double?)[] Drive(DateTime start) =>
    [
        (start, 51.50, 50),
        (start.AddMinutes(5), 51.55, 50),
        (start.AddMinutes(10), 51.60, 50),
        (start.AddMinutes(11), 51.60, 0),
    ];

    [Fact]
    public async Task SavedTrip_CarriesTheOdometerFromBeforeItLeftToAfterItParked()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        await s.AddFixesAsync(Drive(T0), ct);
        await s.AddOdometerAsync(T0.AddDays(-1), 24000.0, ct);   // stale, superseded
        await s.AddOdometerAsync(T0.AddMinutes(-2), 24010.0, ct); // parked before leaving
        await s.AddOdometerAsync(T0.AddMinutes(5), 24015.0, ct);  // mid-drive
        await s.AddOdometerAsync(T0.AddMinutes(12), 24021.4, ct); // reported after parking
        await s.AddOdometerAsync(T0.AddHours(3), 24050.0, ct);    // the next trip's, too late to count

        await s.Recorder.RecordAsync(s.Vehicle.Id, T0.AddHours(4), ct);

        var trip = Assert.Single(await s.SavedAsync(ct));
        Assert.Equal(24010.0, trip.OdometerStartKm);
        Assert.Equal(24021.4, trip.OdometerEndKm);
        Assert.Equal((51.50, 51.60), (trip.StartLatitude, trip.EndLatitude));
    }

    [Fact]
    public async Task SavedTrip_WithoutAnyOdometerReport_HasNone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        await s.AddFixesAsync(Drive(T0), ct);

        await s.Recorder.RecordAsync(s.Vehicle.Id, T0.AddHours(1), ct);

        var trip = Assert.Single(await s.SavedAsync(ct));
        Assert.Null(trip.OdometerStartKm);
        Assert.Null(trip.OdometerEndKm);
    }

    [Fact]
    public async Task NoFixes_SavesNothingAndSetsNoLine()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();

        Assert.Equal(0, await s.Recorder.RecordAsync(s.Vehicle.Id, T0, ct));
        Assert.Null(await s.Trips.GetRecordedUntilAsync(s.Vehicle.Id, ct));
    }

    [Fact]
    public async Task FirstRun_SavesTheWholeHistoryAWeekAtATime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        await s.AddFixesAsync([.. Drive(T0), .. Drive(T0.AddDays(10)), .. Drive(T0.AddDays(20))], ct);
        var now = T0.AddDays(30);

        Assert.Equal(3, await s.Recorder.RecordAsync(s.Vehicle.Id, now, ct));

        Assert.Equal([T0, T0.AddDays(10), T0.AddDays(20)], (await s.SavedAsync(ct)).Select(t => t.StartedAt));
        Assert.Equal(now - TripRecorder.SettleTime, await s.Trips.GetRecordedUntilAsync(s.Vehicle.Id, ct));
    }

    [Fact]
    public async Task TripAcrossAWeekBoundary_IsSavedOnceAndWhole()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        // The first week of history ends between the second drive's first and last fix.
        var crossing = T0 + TripRecorder.Chunk - TimeSpan.FromMinutes(7);
        await s.AddFixesAsync([.. Drive(T0), .. Drive(crossing)], ct);

        Assert.Equal(2, await s.Recorder.RecordAsync(s.Vehicle.Id, T0.AddDays(10), ct));

        var saved = await s.SavedAsync(ct);
        Assert.Equal(2, saved.Count);
        Assert.Equal(crossing, saved[1].StartedAt);
        Assert.Equal(3, saved[1].PointCount);
    }

    [Fact]
    public async Task TripBeingDriven_IsSavedOnlyOnceItHasEnded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        await s.AddFixesAsync([.. Drive(T0).Take(3)], ct);

        Assert.Equal(0, await s.Recorder.RecordAsync(s.Vehicle.Id, T0.AddMinutes(12), ct));
        Assert.Empty(await s.SavedAsync(ct));

        await s.AddFixesAsync([.. Drive(T0).Skip(3)], ct);

        // Parked at minute 11, which is final five minutes later, once that fix has settled.
        Assert.Equal(0, await s.Recorder.RecordAsync(s.Vehicle.Id, T0.AddMinutes(16), ct));
        Assert.Equal(1, await s.Recorder.RecordAsync(s.Vehicle.Id, T0.AddMinutes(17), ct));
        Assert.Equal(3, Assert.Single(await s.SavedAsync(ct)).PointCount);
    }

    [Fact]
    public async Task UnfinishedTrip_HoldsTheLineAtItsStart()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        var second = T0.AddHours(2);
        await s.AddFixesAsync([.. Drive(T0), .. Drive(second).Take(2)], ct);

        Assert.Equal(1, await s.Recorder.RecordAsync(s.Vehicle.Id, second.AddMinutes(6), ct));

        Assert.Equal(second, await s.Trips.GetRecordedUntilAsync(s.Vehicle.Id, ct));
    }

    [Fact]
    public async Task RunningAgain_SavesNothingTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        await s.AddFixesAsync(Drive(T0), ct);

        await s.Recorder.RecordAsync(s.Vehicle.Id, T0.AddHours(1), ct);
        Assert.Equal(0, await s.Recorder.RecordAsync(s.Vehicle.Id, T0.AddHours(2), ct));

        Assert.Single(await s.SavedAsync(ct));
    }

    [Fact]
    public async Task SegmentThatNeverEnds_IsCutAtTheWeekInsteadOfStallingTheLine()
    {
        // A fix every ten minutes, always moving, for eight days: never parked, never quiet.
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        var fixes = Enumerable.Range(0, 8 * 24 * 6)
            .Select(i => (T0.AddMinutes(10 * i), 51.0 + i * 0.001, (double?)20))
            .ToArray();
        await s.AddFixesAsync(fixes, ct);

        Assert.Equal(1, await s.Recorder.RecordAsync(s.Vehicle.Id, T0.AddDays(8), ct));

        Assert.Equal(T0 + TripRecorder.Chunk, await s.Trips.GetRecordedUntilAsync(s.Vehicle.Id, ct));
    }
}
