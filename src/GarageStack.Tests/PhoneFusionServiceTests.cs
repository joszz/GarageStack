using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class PhoneFusionServiceTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Start.AddHours(3);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(Start.AddHours(1));
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Client : IDawarichClient
    {
        public int Calls { get; private set; }
        /// <summary>Thrown by every call while set.</summary>
        public DawarichException? Failure { get; set; }
        /// <summary>Thrown by the calls for trips starting at these times, before <see cref="Failure"/>.</summary>
        public Dictionary<DateTime, DawarichException> FailuresByTrip { get; } = [];
        public DawarichHealth Health { get; set; } = DawarichHealth.Unreachable;

        public Task<IReadOnlyList<PhoneFix>> GetPointsAsync(DateTime from, DateTime to, CancellationToken ct)
        {
            Calls++;
            foreach (var (tripStart, failure) in FailuresByTrip)
                if (from <= tripStart && tripStart <= to) throw failure;
            if (Failure is not null) throw Failure;
            return Task.FromResult<IReadOnlyList<PhoneFix>>([.. new[] { Start, Later }.SelectMany(at => new PhoneFix[]
            {
                new(at, 52, 5, 5, 40, "fake-phone"),
                new(at.AddMinutes(1), 52.005, 5, 5, 40, "fake-phone"),
                new(at.AddMinutes(2), 52.01, 5, 5, 40, "fake-phone"),
            })]);
        }

        public Task<DawarichHealth> CheckAsync(CancellationToken ct) => Task.FromResult(Health);
        public Task<IReadOnlyList<PhoneTracker>> GetTrackersAsync(DateTime since, CancellationToken ct) => throw new NotSupportedException();
    }

    // One linked phone and a two-minute trip starting at each of the given times.
    private static async Task<(ServiceProvider Provider, PhoneFusionService Worker, Clock Clock)> SetupAsync(
        Client client, CancellationToken ct, params DateTime[] trips)
    {
        var name = Guid.NewGuid().ToString();
        var clock = new Clock();
        var services = new ServiceCollection().AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();
        services.AddScoped<IPhoneFusionRepository, PhoneFusionRepository>();
        services.AddSingleton<IDawarichClient>(client);
        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Vehicles.Add(new() { Id = 1, Vin = "FAKEVN00000000001" });
            db.VehiclePhoneTrackers.Add(new() { VehicleId = 1, TrackerId = "fake-phone", CreatedAt = Start.AddDays(-1) });
            await db.SaveChangesAsync(ct);
            var repo = new TripRepository(db, new TelemetryRepository(db));
            await repo.SaveRecordedAsync(1, [.. trips.Select(at => new RecordedTrip(new(0, at, at.AddMinutes(2), 1, 2,
                [new(at, 52, 5, 40), new(at.AddMinutes(2), 52.01, 5, 40)]), null, null))], trips[^1].AddMinutes(3), ct);
        }
        var worker = new PhoneFusionService(NullLogger<PhoneFusionService>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new() { Url = "https://fake.invalid", ApiKey = "fake" }, clock);
        return (provider, worker, clock);
    }

    private static async Task<Dictionary<DateTime, Trip>> TripsAsync(ServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Trips.AsNoTracking()
            .ToDictionaryAsync(t => t.StartedAt, ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureBacksOffAndRecoveryFusesWithoutEditing(bool badKey)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = new Client { Failure = new("Synthetic failure", badKey) };
        var (provider, worker, clock) = await SetupAsync(client, ct, Start);
        await using var _ = provider;
        clock.Now = new(Start.AddMinutes(16));
        await worker.RunPassAsync(ct);
        Assert.Equal(0, client.Calls);
        clock.Now = new(Start.AddHours(1));
        await worker.RunPassAsync(ct);
        await worker.RunPassAsync(ct);
        Assert.Equal(1, client.Calls);
        clock.Now = clock.Now.AddMinutes(31);
        client.Failure = null;
        await worker.RunPassAsync(ct);
        Assert.Equal(2, client.Calls);
        var saved = Assert.Single((await TripsAsync(provider, ct)).Values);
        Assert.Equal(PhoneFusionOutcome.Fused, saved.PhoneFusion);
        Assert.Null(saved.EditedAt);
        Assert.Null(saved.OriginalPointsJson);
    }

    [Fact]
    public async Task AnInvalidAnswerFailsThatTripAndTheNextOneIsNotHeldUp()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = new Client();
        client.FailuresByTrip[Start] = new("Synthetic invalid data", retryable: false);
        var (provider, worker, clock) = await SetupAsync(client, ct, Start, Later);
        await using var _ = provider;
        clock.Now = new(Later.AddHours(1));

        await worker.RunPassAsync(ct);

        var trips = await TripsAsync(provider, ct);
        Assert.Equal(PhoneFusionOutcome.Failed, trips[Start].PhoneFusion);
        Assert.Equal(PhoneFusionOutcome.Fused, trips[Later].PhoneFusion);
    }

    [Fact]
    public async Task ATripThatKeepsFailingWhileDawarichIsUpIsSkippedThenGivenUp()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = new Client { Health = DawarichHealth.Ok };
        client.FailuresByTrip[Start] = new("Synthetic timeout");
        var (provider, worker, clock) = await SetupAsync(client, ct, Start, Later);
        await using var _ = provider;
        clock.Now = new(Later.AddHours(1));

        await worker.RunPassAsync(ct);
        var trips = await TripsAsync(provider, ct);
        Assert.Null(trips[Start].PhoneFusion);
        Assert.Equal(PhoneFusionOutcome.Fused, trips[Later].PhoneFusion);

        for (var pass = 1; pass < PhoneFusionService.MaxTripAttempts; pass++)
            await worker.RunPassAsync(ct);
        Assert.Equal(PhoneFusionOutcome.Failed, (await TripsAsync(provider, ct))[Start].PhoneFusion);
        Assert.Equal(PhoneFusionService.MaxTripAttempts + 1, client.Calls);
    }

    [Fact]
    public async Task AnOutageCountsAgainstNoTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = new Client { Failure = new("Synthetic outage") };
        var (provider, worker, clock) = await SetupAsync(client, ct, Start);
        await using var _ = provider;
        clock.Now = new(Start.AddHours(1));

        for (var pass = 0; pass < PhoneFusionService.MaxTripAttempts + 2; pass++)
        {
            await worker.RunPassAsync(ct);
            clock.Now = clock.Now.AddMinutes(6);
        }

        Assert.Null(Assert.Single((await TripsAsync(provider, ct)).Values).PhoneFusion);
    }
}
