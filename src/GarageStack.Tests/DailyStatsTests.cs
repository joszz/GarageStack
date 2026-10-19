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

    private static TelemetryHistoryPoint Reading(DateTime utc, double? soc = null, double? volts = null, double? counter = null) =>
        new(utc, null, soc, counter, volts, null, null, null, null, null, null, null, null, null, null, null);

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

    [Fact]
    public void UtcRange_SpansTheLocalDays()
    {
        var (start, end) = DailyStatsBuilder.UtcRange(new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25), Amsterdam);

        // Summer time ends on the 25th, so that day lasts 25 hours.
        Assert.Equal(new DateTime(2026, 10, 23, 22, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc), end);
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
