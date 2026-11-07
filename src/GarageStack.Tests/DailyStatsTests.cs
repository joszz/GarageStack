using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GarageStack.Tests;

public class DailyCounterTotalTests
{
    // The same cases as the frontend's dailyCounterTotal, which this replaces for the statistics page.
    [Theory]
    [InlineData(new double[] { }, false, null)]
    [InlineData(new double[] { }, true, null)]
    [InlineData(new[] { 2.1, 9.8, 16.3, 16.3 }, false, 16.3)]
    // 21.4 kWh carried over from yesterday, reset after midnight, then driving resumed.
    [InlineData(new[] { 21.4, 0, 1.2, 4.5 }, true, 4.5)]
    // 0.03 is above 5 % of the 0.5 peak, so only the noise floor flags it as a reset.
    [InlineData(new[] { 0.5, 0.03, 0.8 }, true, 0.8)]
    [InlineData(new[] { 18.7, 0 }, true, null)]
    [InlineData(new[] { 10, 12.5, 14 }, true, 4.0)]
    [InlineData(new[] { 7.3, 7.3 }, true, null)]
    // Hundredths of a litre: yesterday's 907.7 carried over, reset, then 394 burned today.
    [InlineData(new[] { 907.7, 0, 106.6, 394 }, true, 394.0)]
    [InlineData(new[] { 106.6, 394 }, false, 394.0)]
    public void CounterTotal_ReadsADaysCounterLikeThePageDid(double[] readings, bool isPartialDay, double? expected)
    {
        Assert.Equal(expected, DailyStatsBuilder.CounterTotal(readings, isPartialDay));
    }
}

public class DailyStatsBuilderTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static TelemetryHistoryPoint Reading(
        DateTime utc, double? soc = null, double? volts = null, double? counter = null, double? distance = null) =>
        new(utc, null, soc, counter, volts, null, null, null, null, null, null, distance, null, null, null, null);

    [Fact]
    public void AReadingBelongsToItsLocalDay()
    {
        var builder = new DailyStatsBuilder(Amsterdam, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 11), Today);

        // 22:30 UTC on the 10th is 00:30 on the 11th in Amsterdam (summer time, UTC+2).
        builder.Add(Reading(new DateTime(2026, 10, 10, 21, 30, 0, DateTimeKind.Utc), soc: 40));
        builder.Add(Reading(new DateTime(2026, 10, 10, 22, 30, 0, DateTimeKind.Utc), soc: 80));

        var stats = builder.Build();
        Assert.Equal([40.0, 80.0], stats.Select(s => s.EvSocPercent));
    }

    [Fact]
    public void ADayAveragesEveryReadingOfIt()
    {
        var builder = new DailyStatsBuilder(Amsterdam, Today.AddDays(-1), Today.AddDays(-1), Today);
        var morning = new DateTime(2026, 10, 11, 6, 0, 0, DateTimeKind.Utc);

        builder.Add(Reading(morning, soc: 50, volts: 12.4));
        builder.Add(Reading(morning.AddHours(1), soc: 60));
        builder.Add(Reading(morning.AddHours(2), soc: 70, volts: 12.6));

        var day = Assert.Single(builder.Build());
        Assert.Equal(60, day.EvSocPercent);
        Assert.Equal(12.5, day.BatteryVoltage!.Value, 3);
        Assert.Null(day.FuelLevelPercent);
    }

    // Every day of the period comes back, so a chart's axis spans it evenly; the future does not.
    [Fact]
    public void EveryDayUpToTodayComesBack_EvenWithoutReadings()
    {
        var builder = new DailyStatsBuilder(Amsterdam, Today.AddDays(-2), Today.AddDays(5), Today);

        var stats = builder.Build();

        Assert.Equal([Today.AddDays(-2), Today.AddDays(-1), Today], stats.Select(s => s.Date));
        Assert.All(stats, s => Assert.Null(s.CounterTotal));
    }

    // Only today's counter can still hold yesterday's total.
    [Fact]
    public void TodaysCounterIsReadAsAPartialDay()
    {
        var builder = new DailyStatsBuilder(Amsterdam, Today.AddDays(-1), Today, Today);
        var yesterday = new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc);
        var today = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);

        builder.Add(Reading(yesterday, counter: 3));
        builder.Add(Reading(yesterday.AddHours(8), counter: 9));
        builder.Add(Reading(today, counter: 9));
        builder.Add(Reading(today.AddHours(1), counter: 12));

        Assert.Equal([9.0, 3.0], builder.Build().Select(s => s.CounterTotal));
    }

    // The distance counter pairs with the energy counter, so it is read the same way: a finished
    // day by its peak, today by what came after the reset. A day the car stood still is 0 km, so
    // its standby use still counts against the period's distance.
    [Fact]
    public void TheDistanceCounterIsReadLikeTheEnergyCounter()
    {
        var builder = new DailyStatsBuilder(Amsterdam, Today.AddDays(-2), Today, Today);
        var parked = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
        var yesterday = new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc);
        var today = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);

        builder.Add(Reading(parked, distance: 0));
        builder.Add(Reading(parked.AddHours(8), distance: 0));
        builder.Add(Reading(yesterday, distance: 12));
        builder.Add(Reading(yesterday.AddHours(8), distance: 30));
        builder.Add(Reading(today, distance: 30));
        builder.Add(Reading(today.AddHours(1), distance: 0));
        builder.Add(Reading(today.AddHours(2), distance: 5));

        Assert.Equal([0.0, 30.0, 5.0], builder.Build().Select(s => s.DistanceKm));
    }

    [Theory]
    [InlineData(new double[] { }, true, null)]
    [InlineData(new double[] { }, false, null)]
    // Yesterday's total carried over and no driving yet: nothing driven today, not unknown.
    [InlineData(new[] { 30.0, 30.0 }, true, 0.0)]
    [InlineData(new[] { 0.0, 0.0 }, false, 0.0)]
    [InlineData(new[] { 1.0, 2.0, 4.0 }, false, 4.0)]
    public void DistanceTotal_IsZeroRatherThanNullForADayWithoutDriving(double[] readings, bool isPartialDay, double? expected)
    {
        Assert.Equal(expected, DailyStatsBuilder.DistanceTotal(readings, isPartialDay));
    }

    [Fact]
    public void UtcRange_SpansTheLocalDays()
    {
        var (start, end) = DailyStatsBuilder.UtcRange(new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25), Amsterdam);

        // Summer time ends on the 25th, so that day lasts 25 hours.
        Assert.Equal(new DateTime(2026, 10, 23, 22, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc), end);
    }
}

public class DailyEnergyCheckTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static TelemetryHistoryPoint Reading(DateTime utc, double? counter, double? since, bool? charging = null) =>
        new(utc, null, null, counter, null, null, charging, null, null, null, null, null, null, null, null, since);

    private static IReadOnlyList<DailyStat> Build(DateOnly from, DateOnly to, params TelemetryHistoryPoint[] readings)
    {
        var builder = new DailyStatsBuilder(TimeZoneInfo.Utc, from, to, Today);
        foreach (var reading in readings) builder.Add(reading);
        return builder.Build();
    }

    // Issue #551: an MG ZS EV Long Range (2022), RecordedAt, PowerUsageOfDay and
    // PowerUsageSinceLastCharge as the reporter exported them. The day counter gains 33 to 35 kWh
    // at some polls while parked; the since-last-charge counter follows the state of charge.
    private static readonly (DateTime At, double Counter, double Since)[] ZsEv =
    [
        (new DateTime(2026, 11, 2, 13, 29, 42, DateTimeKind.Utc), 33.16, 8.86),
        (new DateTime(2026, 11, 2, 13, 30, 23, DateTimeKind.Utc), 33.16, 8.86),
        (new DateTime(2026, 11, 2, 13, 32, 35, DateTimeKind.Utc), 66.23, 8.86),
        (new DateTime(2026, 11, 2, 13, 34, 46, DateTimeKind.Utc), 66.23, 8.86),
        (new DateTime(2026, 11, 2, 13, 36, 58, DateTimeKind.Utc), 66.23, 8.86),
        (new DateTime(2026, 11, 2, 13, 39, 6, DateTimeKind.Utc), 66.23, 8.86),
        (new DateTime(2026, 11, 2, 18, 40, 37, DateTimeKind.Utc), 66.42, 8.95),
        (new DateTime(2026, 11, 2, 18, 41, 15, DateTimeKind.Utc), 66.42, 8.95),
        (new DateTime(2026, 11, 2, 18, 41, 53, DateTimeKind.Utc), 66.42, 8.95),
        (new DateTime(2026, 11, 2, 18, 42, 32, DateTimeKind.Utc), 66.42, 8.86),
        (new DateTime(2026, 11, 2, 18, 43, 10, DateTimeKind.Utc), 66.42, 8.86),
        (new DateTime(2026, 11, 2, 18, 43, 49, DateTimeKind.Utc), 66.42, 8.86),
        (new DateTime(2026, 11, 2, 18, 46, 0, DateTimeKind.Utc), 99.48, 8.86),
        (new DateTime(2026, 11, 2, 18, 48, 9, DateTimeKind.Utc), 99.48, 8.86),
        (new DateTime(2026, 11, 2, 18, 50, 21, DateTimeKind.Utc), 99.48, 8.86),
        (new DateTime(2026, 11, 2, 18, 52, 33, DateTimeKind.Utc), 99.48, 8.86),
        (new DateTime(2026, 11, 2, 20, 12, 39, DateTimeKind.Utc), 134.24, 9.61),
        (new DateTime(2026, 11, 3, 20, 12, 51, DateTimeKind.Utc), 34.67, 10.46),
        (new DateTime(2026, 11, 3, 21, 1, 6, DateTimeKind.Utc), 34.67, 10.46),
        (new DateTime(2026, 11, 3, 21, 2, 7, DateTimeKind.Utc), 34.67, 10.46),
        (new DateTime(2026, 11, 3, 21, 4, 19, DateTimeKind.Utc), 34.67, 10.46),
        (new DateTime(2026, 11, 3, 21, 6, 30, DateTimeKind.Utc), 34.67, 10.46),
        (new DateTime(2026, 11, 3, 21, 8, 38, DateTimeKind.Utc), 69.43, 10.55),
        (new DateTime(2026, 11, 3, 21, 9, 16, DateTimeKind.Utc), 69.52, 10.55),
        (new DateTime(2026, 11, 3, 21, 9, 55, DateTimeKind.Utc), 69.62, 10.55),
        (new DateTime(2026, 11, 3, 21, 10, 33, DateTimeKind.Utc), 69.71, 10.65),
        (new DateTime(2026, 11, 3, 21, 11, 11, DateTimeKind.Utc), 69.71, 10.65),
        (new DateTime(2026, 11, 3, 21, 11, 49, DateTimeKind.Utc), 69.71, 10.65),
        (new DateTime(2026, 11, 3, 21, 12, 27, DateTimeKind.Utc), 69.71, 10.65),
        (new DateTime(2026, 11, 3, 21, 13, 5, DateTimeKind.Utc), 69.71, 10.65),
        (new DateTime(2026, 11, 3, 21, 13, 43, DateTimeKind.Utc), 69.71, 10.65),
        (new DateTime(2026, 11, 3, 21, 14, 21, DateTimeKind.Utc), 69.71, 10.65),
        (new DateTime(2026, 11, 3, 21, 14, 59, DateTimeKind.Utc), 69.71, 10.65),
        (new DateTime(2026, 11, 3, 21, 15, 38, DateTimeKind.Utc), 69.71, 10.65),
        (new DateTime(2026, 11, 3, 21, 31, 20, DateTimeKind.Utc), 141.12, 11.12),
        (new DateTime(2026, 11, 3, 21, 33, 32, DateTimeKind.Utc), 141.12, 11.12),
        (new DateTime(2026, 11, 3, 21, 35, 43, DateTimeKind.Utc), 141.12, 11.12),
        (new DateTime(2026, 11, 3, 21, 37, 54, DateTimeKind.Utc), 141.12, 11.12),
        (new DateTime(2026, 11, 3, 21, 40, 6, DateTimeKind.Utc), 141.12, 11.12),
        (new DateTime(2026, 11, 3, 23, 8, 51, DateTimeKind.Utc), 141.12, 11.12),
    ];

    [Fact]
    public void ZsEv_ADayCounterThatOutrunsTheBattery_IsFlagged_AndTheBatteryCounterGivesTheDay()
    {
        var stats = Build(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3),
            [.. ZsEv.Select(r => Reading(r.At, r.Counter, r.Since, charging: false))]);

        Assert.Equal((134.24, 0.75, true), (stats[0].CounterTotal, stats[0].SinceChargeTotal, stats[0].CounterUnbacked));
        // The 3rd also gets the 0.85 kWh the car lost while parked since the evening before.
        Assert.Equal((141.12, 1.51, true), (stats[1].CounterTotal, stats[1].SinceChargeTotal, stats[1].CounterUnbacked));
    }

    // A day with one poll has no rises to compare, but 34.67 kWh is more than the 10.46 kWh that
    // had left the battery since it was last charged.
    [Fact]
    public void ZsEv_ASinglePollDay_IsFlaggedAgainstWhatLeftTheBatterySinceTheLastCharge()
    {
        var stats = Build(new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3),
            Reading(ZsEv[16].At, ZsEv[16].Counter, ZsEv[16].Since),
            Reading(ZsEv[17].At, ZsEv[17].Counter, ZsEv[17].Since));

        var day = Assert.Single(stats);
        Assert.Equal((0.85, true), (day.SinceChargeTotal, day.CounterUnbacked));
    }

    [Fact]
    public void ADayCounterThatRisesWithTheBattery_IsTrusted()
    {
        var day = new DateTime(2026, 11, 2, 7, 0, 0, DateTimeKind.Utc);

        var stat = Assert.Single(Build(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2),
            Reading(day, 0.4, 20.0),
            Reading(day.AddHours(1), 1.9, 21.5),
            Reading(day.AddHours(9), 5.4, 25.0)));

        Assert.Equal((5.4, 5.0, false), (stat.CounterTotal, stat.SinceChargeTotal, stat.CounterUnbacked));
    }

    // Regeneration takes energy back off the since-last-charge counter, which a day counter
    // may leave out: 7.5 kWh against 5.5 kWh net, a day after a charge, is still a sound day.
    [Fact]
    public void Regeneration_DoesNotFlagASoundDayCounter()
    {
        var day = new DateTime(2026, 11, 2, 7, 0, 0, DateTimeKind.Utc);

        var stat = Assert.Single(Build(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2),
            Reading(day, 0.6, 0.5),
            Reading(day.AddHours(1), 7.5, 6.0)));

        Assert.False(stat.CounterUnbacked);
    }

    // Charged at lunch: the since-last-charge counter starts again from zero, and the day can
    // hold more than it does now.
    [Fact]
    public void AChargeDuringTheDay_StartsTheBatteryCounterAgain_WithoutFlaggingTheDay()
    {
        var day = new DateTime(2026, 11, 2, 7, 0, 0, DateTimeKind.Utc);

        var stat = Assert.Single(Build(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2),
            Reading(day, 0, 20.0),
            Reading(day.AddHours(2), 10.0, 30.0),
            Reading(day.AddHours(5), 10.0, 30.0, charging: true),
            Reading(day.AddHours(8), 10.5, 0.5),
            Reading(day.AddHours(10), 13.5, 3.5)));

        Assert.Equal((13.5, 13.5, false), (stat.CounterTotal, stat.SinceChargeTotal, stat.CounterUnbacked));
    }

    [Fact]
    public void ACarWithoutASinceLastChargeCounter_KeepsItsDayCounter()
    {
        var day = new DateTime(2026, 11, 2, 7, 0, 0, DateTimeKind.Utc);

        var stat = Assert.Single(Build(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2),
            Reading(day, 1.0, null),
            Reading(day.AddHours(1), 60.0, null)));

        Assert.Equal((60.0, null, false), (stat.CounterTotal, stat.SinceChargeTotal, stat.CounterUnbacked));
    }
}

public class TelemetryRepositoryDailyStatsTests
{
    [Fact]
    public async Task GetDailyStats_FoldsTheStoredReadingsIntoDays()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        var day = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
        db.TelemetrySnapshots.AddRange(
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = day, EvSocPercent = 50, PowerUsageOfDay = 1.5 },
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = day.AddHours(4), EvSocPercent = 30, PowerUsageOfDay = 4.0 },
            // A position-only row says nothing about the day's figures.
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = day.AddHours(5), Latitude = 52, Longitude = 5 });
        await db.SaveChangesAsync(ct);

        var stats = await new TelemetryRepository(db).GetDailyStatsAsync(
            vehicle.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3), TimeZoneInfo.Utc, new DateOnly(2026, 10, 12), ct);

        Assert.Equal(3, stats.Count);
        var march2 = stats[1];
        Assert.Equal(40, march2.EvSocPercent);
        Assert.Equal(4.0, march2.CounterTotal);
        Assert.Null(stats[0].EvSocPercent);
    }

    // The first day of the span starts its since-last-charge share from the last reading before
    // it, so a day reads the same alone as inside a longer span.
    [Fact]
    public async Task GetDailyStats_StartsTheBatteryCounterFromTheReadingBeforeThePeriod()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        var evening = new DateTime(2026, 11, 2, 20, 12, 0, DateTimeKind.Utc);
        db.TelemetrySnapshots.AddRange(
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = evening.AddHours(-1), PowerUsageSinceLastCharge = 9.0 },
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = evening, PowerUsageSinceLastCharge = 9.61 },
            new TelemetrySnapshot { VehicleId = vehicle.Id, RecordedAt = evening.AddDays(1), PowerUsageOfDay = 34.67, PowerUsageSinceLastCharge = 10.46 });
        await db.SaveChangesAsync(ct);
        var repo = new TelemetryRepository(db);

        var alone = await repo.GetDailyStatsAsync(
            vehicle.Id, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3), TimeZoneInfo.Utc, new DateOnly(2026, 11, 4), ct);
        var inSpan = await repo.GetDailyStatsAsync(
            vehicle.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3), TimeZoneInfo.Utc, new DateOnly(2026, 11, 4), ct);

        Assert.Equal(0.85, Assert.Single(alone).SinceChargeTotal);
        Assert.Equal(0.85, inSpan[1].SinceChargeTotal);
    }
}

public class TelemetryRepositoryDailyStatsCacheTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // 14:00 on 12 October in Amsterdam (UTC+2).
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 12);

    private sealed record Setup(AppDbContext Db, TelemetryRepository Repo, int VehicleId, FixedTimeProvider Clock) : IAsyncDisposable
    {
        public async Task AddReadingAsync(DateTime utc, double soc, CancellationToken ct)
        {
            Db.TelemetrySnapshots.Add(new TelemetrySnapshot { VehicleId = VehicleId, RecordedAt = utc, EvSocPercent = soc });
            await Db.SaveChangesAsync(ct);
        }

        public Task<IReadOnlyList<DailyStat>> StatsAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
            Repo.GetDailyStatsAsync(VehicleId, from, to, Amsterdam, ZoneTime.DateOf(Clock.GetUtcNow().UtcDateTime, Amsterdam), ct);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<Setup> SetupAsync(DateTimeOffset now)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        var clock = new FixedTimeProvider(now);
        var repo = new TelemetryRepository(db, cache: new MemoryCache(new MemoryCacheOptions()), time: clock);
        return new Setup(db, repo, vehicle.Id, clock);
    }

    [Fact]
    public async Task AFinishedDay_IsReadOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await SetupAsync(Now);
        var yesterday = new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc);
        await setup.AddReadingAsync(yesterday, 40, ct);

        var first = await setup.StatsAsync(Today.AddDays(-1), Today, ct);
        // Nothing lands on a finished day in practice; here it shows the answer came from the cache.
        await setup.AddReadingAsync(yesterday.AddHours(1), 80, ct);
        var second = await setup.StatsAsync(Today.AddDays(-1), Today, ct);

        Assert.Equal(40, first[0].EvSocPercent);
        Assert.Equal(40, second[0].EvSocPercent);
    }

    [Fact]
    public async Task Today_IsReadEveryTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await SetupAsync(Now);
        var morning = new DateTime(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc);
        await setup.AddReadingAsync(morning, 40, ct);

        await setup.StatsAsync(Today, Today, ct);
        await setup.AddReadingAsync(morning.AddHours(1), 80, ct);
        var stats = await setup.StatsAsync(Today, Today, ct);

        Assert.Equal(60, Assert.Single(stats).EvSocPercent);
    }

    [Fact]
    public async Task ADayEndedWithinTheHour_IsReadAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        // 00:30 on 12 October in Amsterdam: yesterday ended half an hour ago.
        await using var setup = await SetupAsync(new DateTimeOffset(2026, 10, 11, 22, 30, 0, TimeSpan.Zero));
        var lateEvening = new DateTime(2026, 10, 11, 21, 0, 0, DateTimeKind.Utc);
        await setup.AddReadingAsync(lateEvening, 40, ct);

        await setup.StatsAsync(Today.AddDays(-1), Today, ct);
        // A message folded into the last row before midnight.
        await setup.AddReadingAsync(lateEvening.AddMinutes(59), 80, ct);
        var stats = await setup.StatsAsync(Today.AddDays(-1), Today, ct);

        Assert.Equal(60, stats[0].EvSocPercent);
    }

    [Fact]
    public async Task APeriodWithCachedAndUncachedDays_AnswersAsAColdRead()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var setup = await SetupAsync(Now);
        for (var day = 1; day <= 10; day += 3)
            await setup.AddReadingAsync(new DateTime(2026, 10, day, 9, 0, 0, DateTimeKind.Utc), day * 5, ct);
        var from = new DateOnly(2026, 10, 1);

        // The middle of the period is cached first, then the whole of it is asked for.
        await setup.StatsAsync(new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 7), ct);
        var warm = await setup.StatsAsync(from, Today, ct);
        var cold = await new TelemetryRepository(setup.Db).GetDailyStatsAsync(setup.VehicleId, from, Today, Amsterdam, Today, ct);

        Assert.Equal(cold, warm);
        Assert.Equal(12, warm.Count);
    }
}
