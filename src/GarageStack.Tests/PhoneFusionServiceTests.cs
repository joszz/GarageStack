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
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(Start.AddHours(1));
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Client : IDawarichClient
    {
        public int Calls { get; private set; }
        public DawarichException? Failure { get; set; }
        public Task<IReadOnlyList<PhoneFix>> GetPointsAsync(DateTime from, DateTime to, CancellationToken ct)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            return Task.FromResult<IReadOnlyList<PhoneFix>>([
                new(Start, 52, 5, 5, 40, "fake-phone"),
                new(Start.AddMinutes(1), 52.005, 5, 5, 40, "fake-phone"),
                new(Start.AddMinutes(2), 52.01, 5, 5, 40, "fake-phone"),
            ]);
        }
        public Task<DawarichHealth> CheckAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhoneTracker>> GetTrackersAsync(DateTime since, CancellationToken ct) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureBacksOffAndRecoveryFusesWithoutEditing(bool badKey)
    {
        var ct = TestContext.Current.CancellationToken;
        var name = Guid.NewGuid().ToString();
        var client = new Client { Failure = new("Synthetic failure", badKey) };
        var clock = new Clock();
        var services = new ServiceCollection().AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();
        services.AddScoped<IPhoneFusionRepository, PhoneFusionRepository>();
        services.AddSingleton<IDawarichClient>(client);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Vehicles.Add(new() { Id = 1, Vin = "FAKEVN00000000001" });
            db.VehiclePhoneTrackers.Add(new() { VehicleId = 1, TrackerId = "fake-phone", CreatedAt = Start.AddDays(-1) });
            await db.SaveChangesAsync(ct);
            var trips = new TripRepository(db, new TelemetryRepository(db));
            await trips.SaveRecordedAsync(1, [new(new(0, Start, Start.AddMinutes(2), 1, 2,
                [new(Start, 52, 5, 40), new(Start.AddMinutes(2), 52.01, 5, 40)]), null, null)], Start.AddMinutes(3), ct);
        }
        var worker = new PhoneFusionService(NullLogger<PhoneFusionService>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new() { Url = "https://fake.invalid", ApiKey = "fake" }, clock);
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
        await using var check = provider.CreateAsyncScope();
        var saved = await check.ServiceProvider.GetRequiredService<AppDbContext>().Trips.SingleAsync(ct);
        Assert.Equal(PhoneFusionOutcome.Fused, saved.PhoneFusion);
        Assert.Null(saved.EditedAt);
        Assert.Null(saved.OriginalPointsJson);
    }
}
