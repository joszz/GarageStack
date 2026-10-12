using GarageStack.Api.Services;
using GarageStack.Core.Models;
using GarageStack.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GarageStack.Api.Health;

/// <summary>
/// The Worker's latest heartbeat, as the API's PostgreSQL listener hears it. The Worker has no
/// HTTP listener of its own, so this is how a monitor watching the API learns about it.
/// </summary>
public sealed class WorkerHeartbeatMonitor(TimeProvider clock)
{
    private readonly DateTimeOffset _startedAt = clock.GetUtcNow();
    private (DateTimeOffset At, bool MqttConnected)? _last;
    private readonly Lock _lock = new();

    public void Record(WorkerHeartbeatPayload heartbeat)
    {
        lock (_lock) _last = (clock.GetUtcNow(), heartbeat.MqttConnected);
    }

    /// <summary>
    /// Healthy while heartbeats keep arriving and the Worker is subscribed to the broker. Right
    /// after the API starts there is no heartbeat yet; that reads as starting, not as a failure,
    /// so restarting the API does not page whoever watches it.
    /// </summary>
    public HealthCheckResult Evaluate(TimeSpan maxSilence)
    {
        (DateTimeOffset At, bool MqttConnected)? last;
        lock (_lock) last = _last;
        var now = clock.GetUtcNow();

        if (last is not { } heartbeat)
            return now - _startedAt < maxSilence
                ? HealthCheckResult.Degraded("starting")
                : HealthCheckResult.Unhealthy("no heartbeat");

        if (now - heartbeat.At >= maxSilence) return HealthCheckResult.Unhealthy("no heartbeat");
        return heartbeat.MqttConnected ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("broker disconnected");
    }
}

/// <summary>Whether the API reaches its database.</summary>
public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
}

/// <summary>Whether the API reaches the broker it sends car commands through.</summary>
public sealed class MqttHealthCheck(MqttPublisher publisher) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await publisher.EnsureConnectedAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
}

/// <summary>Whether the Worker runs and is subscribed to the broker, by its heartbeat.</summary>
public sealed class WorkerHealthCheck(WorkerHeartbeatMonitor heartbeats) : IHealthCheck
{
    // The Worker beats every 15 seconds; four missed beats is not a hiccup.
    internal static readonly TimeSpan MaxSilence = TimeSpan.FromMinutes(1);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(heartbeats.Evaluate(MaxSilence));
}
