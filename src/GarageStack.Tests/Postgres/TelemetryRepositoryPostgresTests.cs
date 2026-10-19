using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageStack.Tests;

// The telemetry queries as PostgreSQL runs them: the "any field set" filters built from the model,
// the partial update a merged MQTT message makes, and the NOTIFY every write sends the Api.
public class TelemetryRepositoryPostgresTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static async Task<int> AddVehicleAsync(AppDbContext db, CancellationToken ct)
    {
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        return vehicle.Id;
    }

    [Fact]
    public async Task AMergedMessage_UpdatesOnlyItsOwnFields()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);
        var id = await new TelemetryRepository(db).AddAsync(
            new TelemetrySnapshot { VehicleId = vehicleId, RecordedAt = Now, FuelLevelPercent = 60, BatteryVoltage = 12.6 }, ct);

        await using (var merging = database.CreateContext())
            await new TelemetryRepository(merging).MergeIntoAsync(
                id, new TelemetrySnapshot { VehicleId = vehicleId, FuelLevelPercent = 55, EvSocPercent = 80 }, ct);

        await using var check = database.CreateContext();
        var row = await check.TelemetrySnapshots.SingleAsync(ct);
        Assert.Equal((55.0, 80.0, 12.6), (row.FuelLevelPercent, row.EvSocPercent, row.BatteryVoltage));
    }

    [Fact]
    public async Task AMessageForARowThatIsGone_BecomesARowOfItsOwn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);

        await new TelemetryRepository(db).MergeIntoAsync(
            999_999, new TelemetrySnapshot { VehicleId = vehicleId, RecordedAt = Now, FuelLevelPercent = 70 }, ct);

        await using var check = database.CreateContext();
        Assert.Equal(70, (await check.TelemetrySnapshots.SingleAsync(ct)).FuelLevelPercent);
    }

    [Fact]
    public async Task TheMergedLatest_FillsInThePositionAndTheSchedules_FromOlderRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);
        db.TelemetrySnapshots.Add(new TelemetrySnapshot
        {
            VehicleId = vehicleId,
            RecordedAt = Now.AddDays(-20),
            ChargingScheduleMode = "UNTIL_CONFIGURED_TIME",
        });
        db.TelemetrySnapshots.Add(new TelemetrySnapshot { VehicleId = vehicleId, RecordedAt = Now.AddDays(-10), Latitude = 52.37, Longitude = 4.89 });
        db.TelemetrySnapshots.AddRange(Enumerable.Range(0, 250).Select(i => new TelemetrySnapshot
        {
            VehicleId = vehicleId,
            RecordedAt = Now.AddMinutes(-i),
            EvSocPercent = 70,
            BatteryVoltage = 12.5,
        }));
        await db.SaveChangesAsync(ct);

        var merged = await new TelemetryRepository(db).GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal((70.0, 52.37, 4.89), (merged!.EvSocPercent, merged.Latitude, merged.Longitude));
        Assert.Equal("UNTIL_CONFIGURED_TIME", merged.ChargingScheduleMode);
    }

    [Fact]
    public async Task TheDailyStatistics_ReadEveryChartFieldPerLocalDay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);
        var amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var day = new DateOnly(2026, 3, 2);
        var nine = new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc);
        db.TelemetrySnapshots.AddRange(
            new TelemetrySnapshot { VehicleId = vehicleId, RecordedAt = nine, EvSocPercent = 50, PowerUsageOfDay = 1.5, TyrePressureFrontLeft = 2.4 },
            new TelemetrySnapshot { VehicleId = vehicleId, RecordedAt = nine.AddHours(4), EvSocPercent = 30, PowerUsageOfDay = 4.0 },
            // A position alone says nothing about the day's figures.
            new TelemetrySnapshot { VehicleId = vehicleId, RecordedAt = nine.AddHours(5), Latitude = 52, Longitude = 5 });
        await db.SaveChangesAsync(ct);

        var stats = await new TelemetryRepository(db).GetDailyStatsAsync(vehicleId, day, day, amsterdam, day.AddDays(1), ct);

        var stat = Assert.Single(stats);
        Assert.Equal((40.0, 4.0, 2.4), (stat.EvSocPercent, stat.CounterTotal, stat.TyrePressureFrontLeft));
    }

    [Fact]
    public async Task EveryWrite_TellsTheApi_OverNotify()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var vehicleId = await AddVehicleAsync(db, ct);
        await using var listener = new NpgsqlConnection(database.ConnectionString);
        await listener.OpenAsync(ct);
        var heard = new TaskCompletionSource<NpgsqlNotificationEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.Notification += (_, e) => heard.TrySetResult(e);
        await using (var listen = new NpgsqlCommand($"LISTEN {PgChannels.TelemetryUpdated}", listener))
            await listen.ExecuteNonQueryAsync(ct);

        await new TelemetryRepository(db).AddAsync(new TelemetrySnapshot { VehicleId = vehicleId, RecordedAt = Now, EvSocPercent = 1 }, ct);
        await listener.WaitAsync(TimeSpan.FromSeconds(5), ct);

        var notification = await heard.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal((PgChannels.TelemetryUpdated, vehicleId.ToString()), (notification.Channel, notification.Payload));
    }
}
