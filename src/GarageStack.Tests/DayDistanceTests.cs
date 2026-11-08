using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class DayDistanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Midnight = Now.UtcDateTime.Date;

    [Theory]
    [InlineData(30, 1030, 1000, false)]  // drove 30 km, the counter says 30
    [InlineData(31.5, 1030, 1000, false)] // inside the tolerance
    [InlineData(465, 465.1, 457, true)]   // the counter holds the distance since delivery
    public void IsBeyondOdometer_ComparesWithTheOdometerGain(double counter, double odometer, double windowStart, bool expected) =>
        Assert.Equal(expected, DayDistance.IsBeyondOdometer(counter, odometer, windowStart));

    [Fact]
    public void FromOdometer_IsTheGainSinceTheDayStarted() =>
        Assert.Equal(12.3, DayDistance.FromOdometer(1012.3, 1000));

    [Fact]
    public void FromOdometer_WithoutAStartReading_IsNull() =>
        Assert.Null(DayDistance.FromOdometer(1012.3, null));

    private static async Task<(AppDbContext Db, TelemetryRepository Repo, int VehicleId)> SetupAsync(
        params TelemetrySnapshot[] rows)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        foreach (var row in rows) row.VehicleId = vehicle.Id;
        db.TelemetrySnapshots.AddRange(rows);
        await db.SaveChangesAsync();
        return (db, new TelemetryRepository(db, time: new FixedTimeProvider(Now)), vehicle.Id);
    }

    private static TelemetrySnapshot Odometer(DateTime at, double km) => new() { RecordedAt = at, OdometerKm = km };

    [Fact]
    public async Task GetMergedLatest_LifetimeCounter_IsReplacedByTheOdometerGainToday()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(
            Odometer(Midnight.AddDays(-3), 440),
            Odometer(Midnight.AddHours(-2), 453.2),
            new TelemetrySnapshot { RecordedAt = Midnight.AddHours(10), OdometerKm = 465.1, MileageOfTheDay = 464.9 });
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(11.9, result!.MileageOfTheDay);
    }

    [Fact]
    public async Task GetMergedLatest_LifetimeCounterOnAFreshInstall_StartsFromTheFirstReading()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(
            new TelemetrySnapshot { RecordedAt = Midnight.AddHours(9), OdometerKm = 465.1, MileageOfTheDay = 464.9 },
            new TelemetrySnapshot { RecordedAt = Midnight.AddHours(12), OdometerKm = 465.1, MileageOfTheDay = 464.9 });
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(0, result!.MileageOfTheDay);
    }

    [Fact]
    public async Task GetMergedLatest_DayCounterWithinTheOdometerGain_IsKept()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(
            Odometer(Midnight.AddDays(-3), 24000),
            new TelemetrySnapshot { RecordedAt = Midnight.AddHours(10), OdometerKm = 24030.4, MileageOfTheDay = 29.8 });
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(29.8, result!.MileageOfTheDay);
    }

    [Fact]
    public async Task GetMergedLatest_YesterdaysTotalNotYetReset_IsKept()
    {
        // The car still reports yesterday's 80 km just after midnight. Real, so left alone: the
        // window reaches back far enough to see the odometer move by that much.
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(
            Odometer(Midnight.AddDays(-2), 24000),
            Odometer(Midnight.AddHours(-1), 24080),
            new TelemetrySnapshot { RecordedAt = Midnight.AddMinutes(20), OdometerKm = 24080, MileageOfTheDay = 80 });
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(80, result!.MileageOfTheDay);
    }
}
