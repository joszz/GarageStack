using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Worker.Mqtt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    public Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<DailyStat>> GetDailyStatsAsync(int vehicleId, DateOnly from, DateOnly to, TimeZoneInfo zone, DateOnly today, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<TripPoint>> GetGpsFixesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<DateTime?> GetFirstGpsFixAtAsync(int vehicleId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<double?> GetOdometerAtAsync(int vehicleId, DateTime at, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<StatusReading>> GetStatusReadingsAsync(int vehicleId, int count, CancellationToken ct = default) => throw new NotImplementedException();
    public IAsyncEnumerable<TelemetrySnapshot> StreamAllAsync(int vehicleId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<PositionFix?> GetLatestPositionAsync(int vehicleId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string?> GetChargingStopReasonAsync(int vehicleId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<VehicleAggregateStats> GetAggregateStatsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// Telemetry merging and the engine-start notification
// ---------------------------------------------------------------------------

public class TelemetryHandlerTests
{
    private static TelemetryHandler CreateHandler(IPushSender push) =>
        new(NullLogger.Instance, new VehicleResolver(new FakeServiceScopeFactory()), push, WorkerLocalizer.Notifications());

    // ObserveEngineAsync's cooldown gate checks the notification history in this db; an empty
    // in-memory one is enough, since none of these tests seeds a notification.
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
            handler.MergeOrAddTelemetryAsync(1, patch1, repo, ct),
            handler.MergeOrAddTelemetryAsync(1, patch2, repo, ct));

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
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, new NotificationRepository(db), ct);

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
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, new NotificationRepository(db), ct);
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, new NotificationRepository(db), ct);

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
        await handler.ObserveEngineAsync("VIN1", snap, new NotificationRepository(db), ct);
        await handler.ObserveEngineAsync("VIN1", snap, new NotificationRepository(db), ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task ObserveEngine_EngineOff_DoesNotSendNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, new NotificationRepository(db), ct);

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
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, new NotificationRepository(db), ct);
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, new NotificationRepository(db), ct);
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, new NotificationRepository(db), ct);

        Assert.Single(push.Sent);
    }

    [Fact]
    public async Task ObserveEngine_NullEngineRunning_SkipsCheck()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = null }, new NotificationRepository(db), ct);

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
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, new NotificationRepository(db), ct);
        await handler.ObserveEngineAsync("VIN2", new TelemetrySnapshot { EngineRunning = false }, new NotificationRepository(db), ct);
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, new NotificationRepository(db), ct);
        await handler.ObserveEngineAsync("VIN2", new TelemetrySnapshot { EngineRunning = true }, new NotificationRepository(db), ct);

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

        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, new NotificationRepository(db), ct);
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, new NotificationRepository(db), ct); // genuine start - sends
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, new NotificationRepository(db), ct); // flap off
        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, new NotificationRepository(db), ct); // flap back on - must not re-send

        Assert.Single(push.Sent);
    }

    [Fact]
    public async Task ObserveEngine_Stop_IsReportedSoTheTripCanEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        var push = new FakePushSender();
        var handler = CreateHandler(push);
        await using var db = CreateTestDb();

        await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, new NotificationRepository(db), ct);
        var stop = await handler.ObserveEngineAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, new NotificationRepository(db), ct);

        Assert.Equal(StateTransition.TurnedOff, stop);
        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task EngineStop_OverMqtt_RecordsWhenAndWhereTheCarParked()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "saic/user@example.com/vehicles/FAKEVN00000000001/drivetrain/running";
        var databaseName = Guid.NewGuid().ToString();
        await using var services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(databaseName))
            .AddScoped<IVehicleRepository, VehicleRepository>()
            .AddScoped<ITelemetryRepository>(sp => new TelemetryRepository(sp.GetRequiredService<AppDbContext>()))
            .AddScoped<INotificationRepository, NotificationRepository>()
            .AddScoped<ICommutePlaceRepository, CommutePlaceRepository>()
            .BuildServiceProvider();
        var handler = new TelemetryHandler(
            NullLogger.Instance,
            new VehicleResolver(services.GetRequiredService<IServiceScopeFactory>()),
            new FakePushSender(),
            WorkerLocalizer.Notifications());

        await handler.TryHandleAsync(new MqttMessage(topic, "true", Retain: false), ct);
        const string vehicleTopic = "saic/user@example.com/vehicles/FAKEVN00000000001";
        await handler.TryHandleAsync(new MqttMessage($"{vehicleTopic}/location/latitude", "52.37", Retain: false), ct);
        await handler.TryHandleAsync(new MqttMessage($"{vehicleTopic}/location/longitude", "4.89", Retain: false), ct);
        var before = DateTime.UtcNow;
        await handler.TryHandleAsync(new MqttMessage(topic, "false", Retain: false), ct);

        await using var scope = services.CreateAsyncScope();
        var vehicle = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Vehicles.SingleAsync(ct);
        Assert.NotNull(vehicle.LastParkedAt);
        Assert.True(vehicle.LastParkedAt >= before);
        Assert.Equal((52.37, 4.89), (vehicle.ParkedLatitude, vehicle.ParkedLongitude));
    }

    // The gateway publishes a poll's basic status, the engine stopping among it, before that poll's
    // GPS fix. The spot it parked on is the fix that follows, not the previous poll's, which at
    // speed lies hundreds of metres back and read as the car having moved while parked.
    [Fact]
    public async Task EngineStop_ThenThatPollsFix_ParksTheCarAtTheFix()
    {
        var ct = TestContext.Current.CancellationToken;
        const string vehicleTopic = "saic/user@example.com/vehicles/FAKEVN00000000001";
        var databaseName = Guid.NewGuid().ToString();
        await using var services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(databaseName))
            .AddScoped<IVehicleRepository, VehicleRepository>()
            .AddScoped<ITelemetryRepository>(sp => new TelemetryRepository(sp.GetRequiredService<AppDbContext>()))
            .AddScoped<INotificationRepository, NotificationRepository>()
            .AddScoped<ICommutePlaceRepository, CommutePlaceRepository>()
            .BuildServiceProvider();
        var handler = new TelemetryHandler(
            NullLogger.Instance,
            new VehicleResolver(services.GetRequiredService<IServiceScopeFactory>()),
            new FakePushSender(),
            WorkerLocalizer.Notifications());

        // Driving, last seen on the previous poll.
        await handler.TryHandleAsync(new MqttMessage($"{vehicleTopic}/drivetrain/running", "true", Retain: false), ct);
        await handler.TryHandleAsync(new MqttMessage($"{vehicleTopic}/location/position", """{"latitude":52.37,"longitude":4.89}""", Retain: false), ct);
        // The parking poll: the stop first, its position after.
        await handler.TryHandleAsync(new MqttMessage($"{vehicleTopic}/drivetrain/running", "false", Retain: false), ct);
        await handler.TryHandleAsync(new MqttMessage($"{vehicleTopic}/location/position", """{"latitude":52.40,"longitude":4.95}""", Retain: false), ct);

        await using var scope = services.CreateAsyncScope();
        var vehicle = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Vehicles.SingleAsync(ct);
        Assert.Equal((52.40, 4.95), (vehicle.ParkedLatitude, vehicle.ParkedLongitude));
    }
}

// The broker hands the Api's own commands back to the Worker, and the gateway publishes a few
// topics GarageStack leaves out on purpose. Neither should read as a topic nobody maps.
public class TelemetryHandlerLoggingTests
{
    [Theory]
    [InlineData("climate/remoteClimateState/set", false)]
    [InlineData("drivetrain/socTarget/set", false)]
    [InlineData("drivetrain/hybrid_electrical_range", false)]
    [InlineData("drivetrain/batteryHeatingStopReason", false)]
    [InlineData("drivetrain/chargingDataAvailable", false)]
    [InlineData("drivetrain/somethingNew", true)]
    public async Task UnmappedTopic_WarnsOnlyWhenItIsNews(string subtopic, bool warns)
    {
        var logger = new RecordingLogger<TelemetryHandler>();
        var handler = new TelemetryHandler(
            logger, new VehicleResolver(new FakeServiceScopeFactory()), new FakePushSender(), WorkerLocalizer.Notifications());

        await handler.TryHandleAsync(
            new MqttMessage($"saic/user/vehicles/FAKEVN00000000001/{subtopic}", "1", Retain: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(warns, logger.Entries.Any(e => e.Level == LogLevel.Warning));
    }

    // The gateway republishes every topic on every poll: a topic it adds later warns once, not
    // once per poll for as long as the Worker runs.
    [Fact]
    public async Task UnmappedTopic_WarnsOncePerTopic()
    {
        var logger = new RecordingLogger<TelemetryHandler>();
        var handler = new TelemetryHandler(
            logger, new VehicleResolver(new FakeServiceScopeFactory()), new FakePushSender(), WorkerLocalizer.Notifications());
        var ct = TestContext.Current.CancellationToken;

        for (var poll = 0; poll < 3; poll++)
            await handler.TryHandleAsync(new MqttMessage("saic/user/vehicles/FAKEVN00000000001/drivetrain/somethingNew", "1", Retain: false), ct);
        await handler.TryHandleAsync(new MqttMessage("saic/user/vehicles/FAKEVN00000000001/drivetrain/somethingElse", "1", Retain: false), ct);

        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    // A known topic with a payload that cannot be read is a different problem from a topic the
    // mapper has never heard of, and says so.
    [Fact]
    public async Task UnreadablePayload_OnAKnownTopic_IsNotCalledUnmapped()
    {
        var logger = new RecordingLogger<TelemetryHandler>();
        var handler = new TelemetryHandler(
            logger, new VehicleResolver(new FakeServiceScopeFactory()), new FakePushSender(), WorkerLocalizer.Notifications());

        await handler.TryHandleAsync(
            new MqttMessage("saic/user/vehicles/FAKEVN00000000001/location/position", "not json", Retain: false),
            TestContext.Current.CancellationToken);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.StartsWith("Unreadable payload", warning.Message);
    }
}

// Reaching and leaving home or work, told for the places that ask for it.
public class PlaceAlertsOverMqttTests
{
    private const string VehicleTopic = "saic/user@example.com/vehicles/FAKEVN00000000001";

    // Home, and a spot two kilometres away from it.
    private const string AtHome = """{"latitude":52.3700,"longitude":4.8900}""";
    private const string NearHome = """{"latitude":52.3710,"longitude":4.8910}""";
    private const string AwayFromHome = """{"latitude":52.3880,"longitude":4.8900}""";

    private sealed class Rig(ServiceProvider services, TelemetryHandler handler, FakePushSender push) : IAsyncDisposable
    {
        public FakePushSender Push => push;

        public IEnumerable<(string Title, string? Category)> PlacePushes =>
            push.Sent.Where(p => p.Category?.StartsWith("place-", StringComparison.Ordinal) == true)
                .Select(p => (p.Title, p.Category));

        public Task SendAsync(string subtopic, string payload, CancellationToken ct) =>
            handler.TryHandleAsync(new MqttMessage($"{VehicleTopic}/{subtopic}", payload, Retain: false), ct);

        public Task EngineAsync(bool running, CancellationToken ct) => SendAsync("drivetrain/running", running ? "true" : "false", ct);

        public Task PositionAsync(string position, CancellationToken ct) => SendAsync("location/position", position, ct);

        // Parked at home, as the last drive left the car, beside a place set up as asked.
        public async Task ParkedAtHomeAsync(CommutePlace place, CancellationToken ct)
        {
            await EngineAsync(false, ct);
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vehicle = await db.Vehicles.SingleAsync(ct);
            vehicle.ParkedLatitude = 52.37;
            vehicle.ParkedLongitude = 4.89;
            place.VehicleId = vehicle.Id;
            db.CommutePlaces.Add(place);
            await db.SaveChangesAsync(ct);
        }

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }

    private static Rig Build()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(databaseName))
            .AddScoped<IVehicleRepository, VehicleRepository>()
            .AddScoped<ITelemetryRepository>(sp => new TelemetryRepository(sp.GetRequiredService<AppDbContext>()))
            .AddScoped<INotificationRepository, NotificationRepository>()
            .AddScoped<ICommutePlaceRepository, CommutePlaceRepository>()
            .BuildServiceProvider();
        var push = new FakePushSender();
        var handler = new TelemetryHandler(
            NullLogger.Instance,
            new VehicleResolver(services.GetRequiredService<IServiceScopeFactory>()),
            push,
            WorkerLocalizer.Notifications());
        return new Rig(services, handler, push);
    }

    private static CommutePlace Home(bool notify = true, string? label = null) => new()
    {
        Kind = CommutePlaceKind.Home,
        Label = label,
        Latitude = 52.37,
        Longitude = 4.89,
        RadiusMeters = 250,
        Notify = notify,
    };

    [Fact]
    public async Task ADriveFromHomeAndBack_TellsTheLeavingOnceItIsOutside_AndTheArrival()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rig = Build();
        await rig.ParkedAtHomeAsync(Home(), ct);

        await rig.EngineAsync(true, ct);
        await rig.PositionAsync(NearHome, ct);
        Assert.Empty(rig.PlacePushes);

        await rig.PositionAsync(AwayFromHome, ct);
        await rig.PositionAsync(AwayFromHome, ct);
        await rig.EngineAsync(false, ct);
        await rig.PositionAsync(AtHome, ct);

        Assert.Equal(
            [("Left home", NotificationCategories.PlaceDeparture), ("Arrived home", NotificationCategories.PlaceArrival)],
            rig.PlacePushes);
    }

    [Fact]
    public async Task AnEngineRunThatNeverLeaves_TellsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rig = Build();
        await rig.ParkedAtHomeAsync(Home(), ct);

        await rig.EngineAsync(true, ct);
        await rig.PositionAsync(NearHome, ct);
        await rig.EngineAsync(false, ct);
        await rig.PositionAsync(AtHome, ct);

        Assert.Empty(rig.PlacePushes);
    }

    [Fact]
    public async Task APlaceThatDoesNotAskForIt_TellsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rig = Build();
        await rig.ParkedAtHomeAsync(Home(notify: false), ct);

        await rig.EngineAsync(true, ct);
        await rig.PositionAsync(AwayFromHome, ct);
        await rig.EngineAsync(false, ct);
        await rig.PositionAsync(AtHome, ct);

        Assert.Empty(rig.PlacePushes);
    }

    [Fact]
    public async Task ANamedPlace_IsToldByItsName()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rig = Build();
        await rig.ParkedAtHomeAsync(Home(label: "Zwolle"), ct);

        await rig.EngineAsync(true, ct);
        await rig.PositionAsync(AwayFromHome, ct);
        await rig.EngineAsync(false, ct);
        await rig.PositionAsync(AtHome, ct);

        Assert.Equal(["Left Zwolle", "Arrived at Zwolle"], rig.PlacePushes.Select(p => p.Title));
    }

    [Fact]
    public async Task ParkingElsewhere_TellsNoArrival()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rig = Build();
        await rig.ParkedAtHomeAsync(Home(), ct);

        await rig.EngineAsync(true, ct);
        await rig.PositionAsync(AwayFromHome, ct);
        await rig.EngineAsync(false, ct);
        await rig.PositionAsync(AwayFromHome, ct);

        Assert.Equal(["Left home"], rig.PlacePushes.Select(p => p.Title));
    }
}
