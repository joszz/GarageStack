using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Worker.Mqtt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// FakePushSender / FakeServiceScopeFactory live in WorkerTestFakes.cs.

// ---------------------------------------------------------------------------
// Fake ITelemetryRepository with a slow AddAsync -- used to widen the race
// window in the merge-state concurrency test below.
// ---------------------------------------------------------------------------

file sealed class SlowFakeTelemetryRepository : ITelemetryRepository
{
    private long _nextId;
    public int AddCount;
    public int MergeCount;

    public async Task<long> AddAsync(TelemetrySnapshot snapshot, CancellationToken ct = default)
    {
        Interlocked.Increment(ref AddCount);
        // Without TelemetryHandler's per-vehicle gate, two concurrent callers would both
        // read an empty _mergeState before either finishes this delay and writes back,
        // and both would end up here instead of the second one merging into the first.
        await Task.Delay(50, ct);
        return Interlocked.Increment(ref _nextId);
    }

    public Task MergeIntoAsync(long rowId, TelemetrySnapshot patch, CancellationToken ct = default)
    {
        Interlocked.Increment(ref MergeCount);
        return Task.CompletedTask;
    }

    public Task<TelemetrySnapshot?> GetLatestAsync(int vehicleId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<TelemetryHistoryPoint>> GetHistoryAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<TripPoint>> GetGpsFixesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<DateTime?> GetFirstGpsFixAtAsync(int vehicleId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<double?> GetOdometerAtAsync(int vehicleId, DateTime at, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<LastTripSummary?> GetLastTripSummaryAsync(int vehicleId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<StatusReading>> GetStatusReadingsAsync(int vehicleId, int count, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<RawTopicStat>> GetRawTopicStatsAsync(int vehicleId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<VehicleAggregateStats> GetAggregateStatsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// Telemetry merging and the engine-start notification
// ---------------------------------------------------------------------------

public class TelemetryHandlerTests
{
    private static TelemetryHandler CreateHandler(IPushSender push) =>
        new(NullLogger.Instance, new VehicleResolver(new FakeServiceScopeFactory()), push, WorkerLocalizer.Notifications());

    // ObserveEngineAsync's cooldown gate checks AppNotifications via this db - an empty in-memory
    // context is enough for these tests since none of them pre-seed a notification row.
    private static AppDbContext CreateTestDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task MergeOrAddTelemetryAsync_ConcurrentCallsSameVehicle_SecondCallMergesInsteadOfRacing()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new SlowFakeTelemetryRepository();
        var handler = CreateHandler(new FakePushSender());

        var now = DateTime.UtcNow;
        var patch1 = new TelemetrySnapshot { VehicleId = 1, RecordedAt = now };
        var patch2 = new TelemetrySnapshot { VehicleId = 1, RecordedAt = now.AddSeconds(1) }; // within the 15s merge window

        // Fire both "concurrently", as two overlapping MQTT dispatches for the same vehicle would.
        await Task.WhenAll(
            handler.MergeOrAddTelemetryAsync(1, patch1, "saic/vin/topic1", repo, ct),
            handler.MergeOrAddTelemetryAsync(1, patch2, "saic/vin/topic2", repo, ct));

        Assert.Equal(1, repo.AddCount);
        Assert.Equal(1, repo.MergeCount);
    }

    [Fact]
    public async Task ObserveEngine_FirstObservationRunning_SeedsWithoutFiring()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        // On restart the car may already be running; the first observation must not fire.
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, db, ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task ObserveEngine_GenuineTransition_SendsPushNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        // Seed the state with the engine off, then observe a start.
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, db, ct);
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, db, ct);

        Assert.Single(push.Sent);
        Assert.Equal("Engine started", push.Sent[0].Title);
    }

    [Fact]
    public async Task ObserveEngine_AlreadyRunning_DoesNotSendAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        var snap = new TelemetrySnapshot { EngineRunning = true };
        // First: seed. Second: no transition. Neither fires.
        await handler.ObserveEngineAsync("VIN1", snap, db, ct);
        await handler.ObserveEngineAsync("VIN1", snap, db, ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task ObserveEngine_EngineOff_DoesNotSendNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, db, ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task ObserveEngine_StartStopStart_SendsOneNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        // Seed (no fire), stop, start -> exactly one notification.
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, db, ct);
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, db, ct);
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, db, ct);

        Assert.Single(push.Sent);
    }

    [Fact]
    public async Task ObserveEngine_NullEngineRunning_SkipsCheck()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = null }, db, ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task ObserveEngine_MultipleVins_TracksStateIndependently()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        // Seed both VINs as off, then start each one independently.
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, db, ct);
        await handler.ObserveEngineAsync("VIN2", new TelemetrySnapshot { EngineRunning = false }, db, ct);
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, db, ct);
        await handler.ObserveEngineAsync("VIN2", new TelemetrySnapshot { EngineRunning = true }, db, ct);

        Assert.Equal(2, push.Sent.Count);
    }

    [Fact]
    public async Task ObserveEngine_RapidFlap_DoesNotSendDuplicateNotification()
    {
        // Regression test for the P0 fix: the engine-start push used to go out on every
        // false->true transition the tracker observed, with no cooldown at all - a flapping
        // EngineRunning signal (MQTT reconnect/replay noise) could fire a burst of duplicate
        // "Engine started" pushes. The cooldown gate must suppress the second genuine transition
        // within its window even though the tracker itself sees it as a real state change.
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, db, ct);
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, db, ct); // genuine start - sends
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, db, ct); // flap off
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, db, ct); // flap back on - must not re-send

        Assert.Single(push.Sent);
    }

    [Fact]
    public async Task ObserveEngine_Stop_IsReportedSoTheTripCanEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, db, ct);
        var stop = await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, db, ct);

        Assert.Equal(StateTransition.TurnedOff, stop);
        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task EngineStop_OverMqtt_RecordsWhenTheCarParked()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "saic/user@example.com/vehicles/FAKEVN00000000001/drivetrain/running";
        var databaseName = Guid.NewGuid().ToString();
        await using var services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(databaseName))
            .AddScoped<IVehicleRepository, VehicleRepository>()
            .AddScoped<ITelemetryRepository>(sp => new TelemetryRepository(sp.GetRequiredService<AppDbContext>()))
            .BuildServiceProvider();
        var handler = new TelemetryHandler(
            NullLogger.Instance,
            new VehicleResolver(services.GetRequiredService<IServiceScopeFactory>()),
            new FakePushSender(),
            WorkerLocalizer.Notifications());

        await handler.TryHandleAsync(new MqttMessage(topic, "true", Retain: false), ct);
        var before = DateTime.UtcNow;
        await handler.TryHandleAsync(new MqttMessage(topic, "false", Retain: false), ct);

        await using var scope = services.CreateAsyncScope();
        var vehicle = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Vehicles.SingleAsync(ct);
        Assert.NotNull(vehicle.LastParkedAt);
        Assert.True(vehicle.LastParkedAt >= before);
    }
}
