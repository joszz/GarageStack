using GarageStack.Api.Services;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GarageStack.Tests;

public class DistanceSinceChargeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTime T0 = Now.UtcDateTime;

    [Theory]
    [InlineData(1042.0, 1000.0, 42.0)]
    [InlineData(1000.0, 1000.0, 0.0)]       // charging, or just done: nothing driven since
    [InlineData(1000.34, 1000.0, 0.3)]      // to a tenth
    [InlineData(999.0, 1000.0, null)]       // an odometer that went backwards is wrong somewhere
    [InlineData(null, 1000.0, null)]
    [InlineData(1000.0, null, null)]
    [InlineData(double.NaN, 1000.0, null)]
    public void Between_IsTheOdometerGainToATenth(double? now, double? atCharge, double? expected) =>
        Assert.Equal(expected, DistanceSinceCharge.Between(now, atCharge));

    [Fact]
    public void ChargingReadingAt_IsTheRowsTime_WhenThePollIsTheRowsOwn() =>
        Assert.Equal(T0, DistanceSinceCharge.ChargingReadingAt(T0, T0));

    [Fact]
    public void ChargingReadingAt_IsThePollsOwnStamp_ForAnOldStateInANewRow() =>
        Assert.Equal(T0.AddDays(-3), DistanceSinceCharge.ChargingReadingAt(T0, T0.AddDays(-3)));

    [Fact]
    public void ChargingReadingAt_WithoutAStamp_IsTheRowsTime() =>
        Assert.Equal(T0, DistanceSinceCharge.ChargingReadingAt(T0, null));

    [Fact]
    public void ChargingReadingAt_IgnoresAStampLaterThanTheRow() =>
        Assert.Equal(T0, DistanceSinceCharge.ChargingReadingAt(T0, T0.AddHours(1)));

    [Fact]
    public void LastChargeAt_IsTheLaterOfTheReportedEndAndTheLastSeenCharging()
    {
        Assert.Equal(T0.AddHours(-1), DistanceSinceCharge.LastChargeAt(T0, T0.AddHours(-1), T0.AddHours(-2)));
        Assert.Equal(T0.AddHours(-1), DistanceSinceCharge.LastChargeAt(T0, T0.AddHours(-2), T0.AddHours(-1)));
    }

    [Fact]
    public void LastChargeAt_WithOnlyOneOfThem_IsThatOne()
    {
        Assert.Equal(T0.AddHours(-1), DistanceSinceCharge.LastChargeAt(T0, T0.AddHours(-1), null));
        Assert.Equal(T0.AddHours(-1), DistanceSinceCharge.LastChargeAt(T0, null, T0.AddHours(-1)));
        Assert.Null(DistanceSinceCharge.LastChargeAt(T0, null, null));
    }

    [Fact]
    public void LastChargeAt_DoesNotBelieveATimeFromTheFuture()
    {
        // A car's clock that runs ahead would put the end of the last charge past every reading.
        Assert.Equal(T0.AddHours(-1), DistanceSinceCharge.LastChargeAt(T0, T0.AddDays(2), T0.AddHours(-1)));
        Assert.Null(DistanceSinceCharge.LastChargeAt(T0, T0.AddDays(2), null));
    }

    [Fact]
    public void LastChargeAt_AllowsAFewMinutesOfClockDifference() =>
        Assert.Equal(T0.AddMinutes(2), DistanceSinceCharge.LastChargeAt(T0, null, T0.AddMinutes(2)));

    [Fact]
    public void TheCalculatedFlag_IsNotAReadingToStoreMergeOrExport()
    {
        const string flag = nameof(TelemetrySnapshot.MileageSinceLastChargeCalculated);

        Assert.DoesNotContain(TelemetryFields.Mergeable, p => p.Name == flag);
        Assert.DoesNotContain(ZipExport.TelemetryColumns, p => p.Name == flag);
    }

    // ── A charge the polls missed ─────────────────────────────────────────────

    private static DistanceSinceCharge.SocReading Reading(double hours, double? odometer, double? soc) =>
        new(T0.AddHours(hours), odometer, soc);

    [Fact]
    public void LastSocRiseCharge_AParkedCarWhoseChargeRoseByTwoPoints_IsACharge()
    {
        var found = DistanceSinceCharge.LastSocRiseCharge(
            [Reading(-5, 2858, 83.3), Reading(-4, 2858, 83.3), Reading(-1, 2858, 90)]);

        Assert.Equal(new DistanceSinceCharge.SocRiseCharge(T0.AddHours(-1), 2858), found);
    }

    [Fact]
    public void LastSocRiseCharge_LessThanTwoPoints_IsNotACharge() =>
        Assert.Null(DistanceSinceCharge.LastSocRiseCharge(
            [Reading(-5, 2858, 83.3), Reading(-1, 2858, 85.2)]));

    [Theory]
    [InlineData(98.3, 100)]   // 1.7 points: short of two, but the car is full
    [InlineData(98.9, 100)]
    [InlineData(99.0, 100)]
    public void LastSocRiseCharge_AParkedCarThatReachesFull_IsACharge_EvenOnALessThanTwoPointClimb(double before, double full)
    {
        var found = DistanceSinceCharge.LastSocRiseCharge([Reading(-5, 2858, before), Reading(-1, 2858, full)]);

        Assert.Equal(new DistanceSinceCharge.SocRiseCharge(T0.AddHours(-1), 2858), found);
    }

    [Fact]
    public void LastSocRiseCharge_ACarAlreadyFullThatStaysFull_IsNotACharge() =>
        Assert.Null(DistanceSinceCharge.LastSocRiseCharge([Reading(-5, 2858, 99.6), Reading(-1, 2858, 100)]));

    [Fact]
    public void LastSocRiseCharge_AClimbOfAPointThatDoesNotReachFull_IsNotACharge() =>
        Assert.Null(DistanceSinceCharge.LastSocRiseCharge([Reading(-5, 2858, 80), Reading(-1, 2858, 81.5)]));

    [Fact]
    public void LastSocRiseCharge_TheSmallWobbleOfAParkedCar_IsNotACharge() =>
        Assert.Null(DistanceSinceCharge.LastSocRiseCharge(
            [Reading(-5, 2858, 83.3), Reading(-4, 2858, 83.6), Reading(-3, 2858, 83.3), Reading(-2, 2858, 83.5)]));

    [Fact]
    public void LastSocRiseCharge_APointOrSoGainedWhileDriving_IsNotACharge() =>
        // Every reading is at a new odometer value, so none is a stretch of a parked car.
        Assert.Null(DistanceSinceCharge.LastSocRiseCharge(
            [Reading(-4, 2840, 83.0), Reading(-3, 2841, 83.9), Reading(-2, 2842, 84.4), Reading(-1, 2843, 86.0)]));

    [Fact]
    public void LastSocRiseCharge_ClimbsCountedFromTheLowestOfTheStretch_NotFromTheLastReading()
    {
        // A slow charge adds a few tenths between reads, which only the whole rise shows.
        var found = DistanceSinceCharge.LastSocRiseCharge(
            [Reading(-5, 2858, 80), Reading(-4, 2858, 80.6), Reading(-3, 2858, 81.2), Reading(-2, 2858, 81.8), Reading(-1, 2858, 82.3)]);

        Assert.Equal(new DistanceSinceCharge.SocRiseCharge(T0.AddHours(-1), 2858), found);
    }

    [Fact]
    public void LastSocRiseCharge_ADropThenARise_CountsTheRiseFromTheDrop()
    {
        var found = DistanceSinceCharge.LastSocRiseCharge(
            [Reading(-5, 2858, 80), Reading(-4, 2858, 78), Reading(-1, 2858, 80.1)]);

        Assert.Equal(T0.AddHours(-1), found!.At);
    }

    [Fact]
    public void LastSocRiseCharge_ACarThatChargedThenWasDriven_IsStillTheCharge()
    {
        var found = DistanceSinceCharge.LastSocRiseCharge(
            [Reading(-9, 2858, 83.3), Reading(-8, 2858, 91), Reading(-6, 2860, 90), Reading(-5, 2870, 85)]);

        Assert.Equal(new DistanceSinceCharge.SocRiseCharge(T0.AddHours(-8), 2858), found);
    }

    [Fact]
    public void LastSocRiseCharge_WithTwoCharges_IsTheLaterOne()
    {
        var found = DistanceSinceCharge.LastSocRiseCharge(
            [Reading(-20, 2800, 50), Reading(-19, 2800, 80), Reading(-10, 2840, 60), Reading(-9, 2840, 62.5)]);

        Assert.Equal(new DistanceSinceCharge.SocRiseCharge(T0.AddHours(-9), 2840), found);
    }

    [Fact]
    public void LastSocRiseCharge_RowsThatCarryOnlyOneOfTheTwo_AreCarriedForward()
    {
        var found = DistanceSinceCharge.LastSocRiseCharge(
            [Reading(-5, 2858, null), Reading(-4, null, 83.3), Reading(-3, null, 84), Reading(-1, null, 90)]);

        Assert.Equal(new DistanceSinceCharge.SocRiseCharge(T0.AddHours(-1), 2858), found);
    }

    [Fact]
    public void LastSocRiseCharge_WithoutAnOdometer_IsNotACharge() =>
        Assert.Null(DistanceSinceCharge.LastSocRiseCharge([Reading(-5, null, 60), Reading(-1, null, 90)]));

    [Fact]
    public void LastSocRiseCharge_InAnyOrder_IsReadInTimeOrder()
    {
        var found = DistanceSinceCharge.LastSocRiseCharge(
            [Reading(-1, 2858, 90), Reading(-5, 2858, 83.3)]);

        Assert.Equal(T0.AddHours(-1), found!.At);
    }

    // ── The repository ───────────────────────────────────────────────────────

    private static async Task<(AppDbContext Db, TelemetryRepository Repo, int VehicleId)> SetupAsync(
        IMemoryCache? cache = null, params TelemetrySnapshot[] rows)
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
        return (db, new TelemetryRepository(db, cache: cache, time: new FixedTimeProvider(Now)), vehicle.Id);
    }

    private static TelemetrySnapshot Odometer(DateTime at, double km) => new() { RecordedAt = at, OdometerKm = km };

    private static TelemetrySnapshot Charging(DateTime at, bool charging, DateTime? stateAt = null) =>
        new() { RecordedAt = at, IsCharging = charging, LastChargeStateAt = stateAt };

    [Fact]
    public async Task GetMergedLatest_CarThatReportsTheCounter_KeepsItsOwnValue()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Odometer(T0.AddDays(-2), 1000),
            Charging(T0.AddDays(-2), true),
            new TelemetrySnapshot { RecordedAt = T0.AddMinutes(-5), OdometerKm = 1080, MileageSinceLastCharge = 77 });
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(77, result!.MileageSinceLastCharge);
        Assert.False(result.MileageSinceLastChargeCalculated);
    }

    [Fact]
    public async Task GetMergedLatest_CarWithoutTheCounter_DrivenSinceItsLastSeenCharge_IsTheOdometerGain()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Odometer(T0.AddDays(-3), 990),
            Odometer(T0.AddDays(-2).AddHours(-1), 1000),
            Charging(T0.AddDays(-2).AddHours(-1), true),
            Charging(T0.AddDays(-2), true),
            Charging(T0.AddDays(-2).AddMinutes(1), false),
            Odometer(T0.AddHours(-5), 1030),
            Odometer(T0.AddMinutes(-5), 1042));
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(42, result!.MileageSinceLastCharge);
        Assert.True(result.MileageSinceLastChargeCalculated);
    }

    [Fact]
    public async Task GetMergedLatest_WhileCharging_IsZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Odometer(T0.AddDays(-2), 1000),
            Charging(T0.AddDays(-2), true),
            Charging(T0.AddDays(-2).AddHours(3), false),
            Odometer(T0.AddHours(-3), 1042),
            Charging(T0.AddMinutes(-20), true),
            Charging(T0.AddMinutes(-5), true));
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(0, result!.MileageSinceLastCharge);
    }

    [Fact]
    public async Task GetMergedLatest_ChargeTooShortToBeSeen_UsesTheEndTimeTheCarReported()
    {
        // No reading ever showed the car charging, but it says when its last charge ended.
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Odometer(T0.AddDays(-1), 2927),
            new TelemetrySnapshot { RecordedAt = T0.AddDays(-1).AddMinutes(10), IsCharging = false, ChargingLastEndAt = T0.AddDays(-1).AddMinutes(3) },
            Odometer(T0.AddMinutes(-5), 2945));
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(18, result!.MileageSinceLastCharge);
    }

    [Fact]
    public async Task GetMergedLatest_WithNoChargeInTheReadings_HasNoDistance()
    {
        // A car's first charge after GarageStack began recording has not happened: nothing to count from.
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Odometer(T0.AddDays(-1), 2834),
            Charging(T0.AddDays(-1), false),
            Odometer(T0.AddMinutes(-5), 2858));
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Null(result!.MileageSinceLastCharge);
        Assert.False(result.MileageSinceLastChargeCalculated);
    }

    [Fact]
    public async Task GetMergedLatest_AnOldChargeStateInANewRow_DoesNotMoveTheEndOfTheLastCharge()
    {
        // The gateway sent its retained topics again when it started: a row from now that says
        // "charging", with the stamp of a poll from days ago. The charge is the old one.
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Odometer(T0.AddDays(-4), 1000),
            Charging(T0.AddDays(-4), true),
            Odometer(T0.AddHours(-2), 1060),
            Charging(T0.AddHours(-1), true, stateAt: T0.AddDays(-4)),
            Odometer(T0.AddMinutes(-5), 1075));
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(75, result!.MileageSinceLastCharge);
    }

    [Fact]
    public async Task GetMergedLatest_ChargeFurtherBackThanTheLookback_IsNotUsed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Odometer(T0 - DistanceSinceCharge.Lookback.Add(TimeSpan.FromDays(2)), 1000),
            Charging(T0 - DistanceSinceCharge.Lookback.Add(TimeSpan.FromDays(2)), true),
            Odometer(T0.AddMinutes(-5), 9000));
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Null(result!.MileageSinceLastCharge);
    }

    [Fact]
    public async Task GetMergedLatest_WithoutAnOdometer_HasNoDistance()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Charging(T0.AddDays(-2), true),
            new TelemetrySnapshot { RecordedAt = T0.AddMinutes(-5), EvSocPercent = 60 });
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Null(result!.MileageSinceLastCharge);
    }

    [Fact]
    public async Task GetMergedLatest_RemembersTheOdometerAtTheLastCharge_ForAMinute()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var (db, repo, vehicleId) = await SetupAsync(cache,
            Odometer(T0.AddDays(-2), 1000),
            Charging(T0.AddDays(-2), true),
            Odometer(T0.AddMinutes(-5), 1042));
        await using var _ = db;

        Assert.Equal(42, (await repo.GetMergedLatestAsync(vehicleId, ct))!.MileageSinceLastCharge);

        // A charge that is seen after the first read does not change the odometer remembered for the
        // last one, however the merged snapshot is built again, until the minute is up.
        db.TelemetrySnapshots.AddRange(
            new TelemetrySnapshot { VehicleId = vehicleId, RecordedAt = T0.AddMinutes(-2), IsCharging = true, OdometerKm = 1042 });
        await db.SaveChangesAsync(ct);
        cache.Remove($"telemetry-latest/{vehicleId}");

        Assert.Equal(42, (await repo.GetMergedLatestAsync(vehicleId, ct))!.MileageSinceLastCharge);
    }

    [Fact]
    public async Task GetMergedLatest_RemembersThatThereWasNoCharge_TooSoThatItIsNotSearchedForOnEveryRead()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var (db, repo, vehicleId) = await SetupAsync(cache, Odometer(T0.AddMinutes(-5), 2858));
        await using var _ = db;

        Assert.Null((await repo.GetMergedLatestAsync(vehicleId, ct))!.MileageSinceLastCharge);

        db.TelemetrySnapshots.Add(new TelemetrySnapshot
        {
            VehicleId = vehicleId,
            RecordedAt = T0.AddMinutes(-1),
            IsCharging = true,
            OdometerKm = 2858,
        });
        await db.SaveChangesAsync(ct);
        cache.Remove($"telemetry-latest/{vehicleId}");

        // Still the cached "no charge": the search is not run again within the minute.
        Assert.Null((await repo.GetMergedLatestAsync(vehicleId, ct))!.MileageSinceLastCharge);
    }

    private static TelemetrySnapshot Soc(DateTime at, double soc, double? odometer = null) =>
        new() { RecordedAt = at, EvSocPercent = soc, OdometerKm = odometer };

    [Fact]
    public async Task GetMergedLatest_ChargeNoReadingCaught_StillCountsFromTheStateOfChargeGoingUp()
    {
        // The Urban was read at 83 %, unplugged, and next read at 90 % at the same odometer: a charge
        // happened between the two reads, and the distance counts from it.
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Soc(T0.AddDays(-2), 83.3, 2858),
            Soc(T0.AddDays(-1), 90),
            Odometer(T0.AddMinutes(-5), 2900));
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(42, result!.MileageSinceLastCharge);
        Assert.True(result.MileageSinceLastChargeCalculated);
    }

    [Fact]
    public async Task GetMergedLatest_AMissedChargeAfterASeenOne_IsTheLastCharge()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Odometer(T0.AddDays(-5), 2800),
            Charging(T0.AddDays(-5), true),
            Soc(T0.AddDays(-4), 60, 2850),
            Soc(T0.AddDays(-3), 85),
            Odometer(T0.AddMinutes(-5), 2900));
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(50, result!.MileageSinceLastCharge);
    }

    [Fact]
    public async Task GetMergedLatest_ASeenChargeAfterAClimbOfTheStateOfCharge_IsTheLastCharge()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Soc(T0.AddDays(-5), 60, 2800),
            Soc(T0.AddDays(-4), 85),
            Odometer(T0.AddDays(-2), 2850),
            Charging(T0.AddDays(-2), true),
            Odometer(T0.AddMinutes(-5), 2900));
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Equal(50, result!.MileageSinceLastCharge);
    }

    [Fact]
    public async Task GetMergedLatest_AClimbOfLessThanTwoPoints_IsNoChargeToCountFrom()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, repo, vehicleId) = await SetupAsync(null,
            Soc(T0.AddDays(-2), 83.3, 2858),
            Soc(T0.AddDays(-1), 84.5),
            Odometer(T0.AddMinutes(-5), 2900));
        await using var _ = db;

        var result = await repo.GetMergedLatestAsync(vehicleId, ct);

        Assert.Null(result!.MileageSinceLastCharge);
    }
}
