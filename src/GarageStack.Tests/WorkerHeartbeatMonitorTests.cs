using GarageStack.Api.Health;
using GarageStack.Core.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
