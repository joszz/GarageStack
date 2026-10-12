using GarageStack.Api.Health;
using GarageStack.Api.Hosting;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Worker.Mqtt;
using GarageStack.Worker.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Events;

namespace GarageStack.Tests;

public class WorkerHeartbeatMonitorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxSilence = TimeSpan.FromMinutes(1);

    [Fact]
    public void NoHeartbeatYet_RightAfterTheApiStarted_IsStartingNotDown()
    {
        var clock = new FixedTimeProvider(Start);
        var monitor = new WorkerHeartbeatMonitor(clock);
        clock.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal(HealthStatus.Degraded, monitor.Evaluate(MaxSilence).Status);
    }

    [Fact]
    public void NoHeartbeatAtAll_AfterAMinute_IsDown()
    {
        var clock = new FixedTimeProvider(Start);
        var monitor = new WorkerHeartbeatMonitor(clock);
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(HealthStatus.Unhealthy, monitor.Evaluate(MaxSilence).Status);
    }

    [Fact]
    public void RecentHeartbeat_Subscribed_IsHealthy()
    {
        var clock = new FixedTimeProvider(Start);
        var monitor = new WorkerHeartbeatMonitor(clock);
        monitor.Record(new WorkerHeartbeatPayload(MqttConnected: true));
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(HealthStatus.Healthy, monitor.Evaluate(MaxSilence).Status);
    }

    [Fact]
    public void RecentHeartbeat_BrokerDisconnected_IsDown()
    {
        var monitor = new WorkerHeartbeatMonitor(new FixedTimeProvider(Start));
        monitor.Record(new WorkerHeartbeatPayload(MqttConnected: false));

        Assert.Equal(HealthStatus.Unhealthy, monitor.Evaluate(MaxSilence).Status);
    }

    // A Worker that stopped or hangs stops beating; the last beat said all was well.
    [Fact]
    public void HeartbeatsStopped_IsDown()
    {
        var clock = new FixedTimeProvider(Start);
        var monitor = new WorkerHeartbeatMonitor(clock);
        monitor.Record(new WorkerHeartbeatPayload(MqttConnected: true));
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(HealthStatus.Unhealthy, monitor.Evaluate(MaxSilence).Status);
    }
}

public class RequestLogLevelTests
{
    private static LogEventLevel LevelFor(string path, int status, Exception? ex = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;
        return ApiHostingExtensions.RequestLogLevel(context, 1, ex);
    }

    // Docker probes /health every ten seconds, and monitors poll /api/health.
    [Theory]
    [InlineData("/health", 200)]
    [InlineData("/api/health", 200)]
    [InlineData("/api/health", 503)]
    public void HealthProbes_StayOutOfTheLogFiles(string path, int status)
    {
        Assert.Equal(LogEventLevel.Verbose, LevelFor(path, status));
    }

    [Fact]
    public void OrdinaryRequests_AreLoggedAsBefore()
    {
        Assert.Equal(LogEventLevel.Information, LevelFor("/api/vehicles", 200));
        Assert.Equal(LogEventLevel.Error, LevelFor("/api/vehicles", 500));
        Assert.Equal(LogEventLevel.Error, LevelFor("/api/health", 200, new InvalidOperationException()));
    }
}

public class WorkerHeartbeatServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"garagestack-heartbeat-test-{Guid.NewGuid():N}");

    public void Dispose() => File.Delete(_file);

    private static IServiceScopeFactory InMemoryDatabase()
    {
        var name = Guid.NewGuid().ToString();
        return new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
    }

    private WorkerHeartbeatService Service(IServiceScopeFactory scopes, bool mqttConnected)
    {
        var mqtt = new MqttConnectionState();
        mqtt.Set(mqttConnected);
        return new WorkerHeartbeatService(NullLogger<WorkerHeartbeatService>.Instance, scopes, mqtt) { FilePath = _file };
    }

    [Fact]
    public async Task Beat_SubscribedAndDatabaseReachable_TouchesTheFile()
    {
        await Service(InMemoryDatabase(), mqttConnected: true).BeatAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(_file));
    }

    [Fact]
    public async Task Beat_BrokerDisconnected_LeavesTheFileToAge()
    {
        await Service(InMemoryDatabase(), mqttConnected: false).BeatAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(_file));
    }

    // FakeServiceScopeFactory resolves no database, which is what an unreachable one looks like here.
    [Fact]
    public async Task Beat_DatabaseUnreachable_LeavesTheFileToAge()
    {
        await Service(new FakeServiceScopeFactory(), mqttConnected: true).BeatAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(_file));
    }
}
