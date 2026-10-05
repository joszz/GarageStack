using GarageStack.Core.Configuration;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Worker.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class TelemetryCompactorTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    // 90 days of full detail from Now: rows before 3 March 2026 may be compacted.
    private static readonly TelemetryRetentionOptions NinetyDays = new() { FullDetailDays = 90 };
    private static readonly DateTime FirstKeptDay = new(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Setup : IAsyncDisposable
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        public Vehicle Vehicle { get; } = new() { Vin = "FAKEVN00000000001", TripsRecordedUntil = Now };

        public Setup()
        {
            Db.Vehicles.Add(Vehicle);
            Db.SaveChanges();
        }

        public async Task AddAsync(CancellationToken ct, params TelemetrySnapshot[] rows)
        {
            foreach (var row in rows) row.VehicleId = Vehicle.Id;
            Db.TelemetrySnapshots.AddRange(rows);
            await Db.SaveChangesAsync(ct);
            Db.ChangeTracker.Clear();
        }

        // Read back the way the Worker does: fresh from the database, untracked.
        public async Task<int> CompactAsync(TelemetryRetentionOptions options, CancellationToken ct)
        {
            var vehicle = await Db.Vehicles.AsNoTracking().SingleAsync(ct);
            return await new TelemetryCompactor(new HousekeepingRepository(Db), options).CompactAsync(vehicle, Now, ct);
        }

        public Task<List<DateTime>> RowTimesAsync(CancellationToken ct) =>
            Db.TelemetrySnapshots.AsNoTracking().OrderBy(s => s.RecordedAt).Select(s => s.RecordedAt).ToListAsync(ct);

        public Task<DateTime?> CompactedUntilAsync(CancellationToken ct) =>
            Db.Vehicles.AsNoTracking().Select(v => v.TelemetryCompactedUntil).SingleAsync(ct);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    // Three polls in one quarter of an hour, starting at `start`.
    private static TelemetrySnapshot[] Polls(DateTime start) =>
    [
        new() { RecordedAt = start, FuelLevelPercent = 50 },
        new() { RecordedAt = start.AddMinutes(5), BatteryVoltage = 12.6 },
        new() { RecordedAt = start.AddMinutes(10), OdometerKm = 1000 },
    ];

    [Fact]
    public async Task OnlyWholeDaysOlderThanTheFullDetailPeriod_AreCompacted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        var oldDay = FirstKeptDay.AddDays(-2).AddHours(10);
        var keptDay = FirstKeptDay.AddHours(10);
        await s.AddAsync(ct, [.. Polls(oldDay), .. Polls(keptDay)]);

        Assert.Equal(2, await s.CompactAsync(NinetyDays, ct));

        Assert.Equal([oldDay.AddMinutes(10), .. Polls(keptDay).Select(p => p.RecordedAt)], await s.RowTimesAsync(ct));
        Assert.Equal(FirstKeptDay, await s.CompactedUntilAsync(ct));
    }

    [Fact]
    public async Task Compaction_NeverPassesTheTripRecordingLine()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        var recordedUntil = new DateTime(2026, 2, 15, 10, 30, 0, DateTimeKind.Utc);
        s.Vehicle.TripsRecordedUntil = recordedUntil;
        await s.Db.SaveChangesAsync(ct);
        await s.AddAsync(ct, [.. Polls(recordedUntil.AddDays(-1)), .. Polls(recordedUntil.AddHours(-1))]);

        Assert.Equal(2, await s.CompactAsync(NinetyDays, ct));

        Assert.Equal(recordedUntil.Date, await s.CompactedUntilAsync(ct));
        Assert.Equal(4, (await s.RowTimesAsync(ct)).Count);
    }

    [Fact]
    public async Task AVehicleWhoseTripsWereNeverRecorded_IsLeftAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        s.Vehicle.TripsRecordedUntil = null;
        await s.Db.SaveChangesAsync(ct);
        await s.AddAsync(ct, Polls(FirstKeptDay.AddDays(-30)));

        Assert.Equal(0, await s.CompactAsync(NinetyDays, ct));

        Assert.Equal(3, (await s.RowTimesAsync(ct)).Count);
        Assert.Null(await s.CompactedUntilAsync(ct));
    }

    [Fact]
    public async Task SwitchedOff_KeepsEveryRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        await s.AddAsync(ct, Polls(FirstKeptDay.AddDays(-300)));

        Assert.Equal(0, await s.CompactAsync(new TelemetryRetentionOptions { FullDetailDays = null }, ct));

        Assert.Equal(3, (await s.RowTimesAsync(ct)).Count);
    }

    [Fact]
    public async Task RunningAgain_FoldsNothingMoreAndKeepsTheLine()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        await s.AddAsync(ct, Polls(FirstKeptDay.AddDays(-10)));

        Assert.Equal(2, await s.CompactAsync(NinetyDays, ct));
        Assert.Equal(0, await s.CompactAsync(NinetyDays, ct));

        Assert.Single(await s.RowTimesAsync(ct));
        Assert.Equal(FirstKeptDay, await s.CompactedUntilAsync(ct));
    }

    [Fact]
    public async Task TheLatestReadings_AtTheEndOfEachWindow_AreTheSameAfterCompaction()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        var start = FirstKeptDay.AddDays(-20).AddHours(10);
        await s.AddAsync(ct,
            new TelemetrySnapshot { RecordedAt = start, OdometerKm = 1000, Latitude = 52.0, Longitude = 5.0 },
            new TelemetrySnapshot { RecordedAt = start.AddMinutes(5), Latitude = 52.1, Longitude = 5.0, Speed = 40 },
            new TelemetrySnapshot { RecordedAt = start.AddMinutes(10), OdometerKm = 1003, FuelLevelPercent = 50 },
            new TelemetrySnapshot { RecordedAt = start.AddMinutes(20), Latitude = 52.2, Longitude = 5.0, Speed = 0 },
            new TelemetrySnapshot { RecordedAt = start.AddMinutes(29), OdometerKm = 1005 });

        // The odometer and the car's last position as of each window's end. A kept row carries its
        // window's last time, so only the readings are compared, not when a fix was taken.
        var telemetry = new TelemetryRepository(s.Db);
        DateTime[] windowEnds = [start.AddMinutes(15).AddTicks(-1), start.AddMinutes(30).AddTicks(-1), start.AddDays(1)];
        async Task<List<(double? Odometer, double? Latitude, double? Longitude)>> ReadAsync()
        {
            var readings = new List<(double?, double?, double?)>();
            foreach (var at in windowEnds)
            {
                var lastFix = (await telemetry.GetGpsFixesAsync(s.Vehicle.Id, start, at, ct)).LastOrDefault();
                readings.Add((await telemetry.GetOdometerAtAsync(s.Vehicle.Id, at, ct), lastFix?.Latitude, lastFix?.Longitude));
            }

            return readings;
        }

        var before = await ReadAsync();
        Assert.Equal(3, await s.CompactAsync(NinetyDays, ct));

        Assert.Equal(before, await ReadAsync());
        Assert.Equal((1005.0, 52.2, 5.0), (before[^1].Odometer!.Value, before[^1].Latitude!.Value, before[^1].Longitude!.Value));
    }
}
