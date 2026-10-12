using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using GarageStack.Worker.Mqtt;

namespace GarageStack.Worker.Services;

/// <summary>
/// Says the Worker is alive, in two ways. A file the Docker healthcheck reads, touched only while
/// the Worker is subscribed to the broker and reaches the database, so a Worker that lost either
/// shows unhealthy instead of merely running. And a notification the API's health check hears,
/// so one URL speaks for the whole stack.
/// </summary>
public sealed class WorkerHeartbeatService(
    ILogger<WorkerHeartbeatService> logger,
    IServiceScopeFactory scopeFactory,
    MqttConnectionState mqtt) : PeriodicBackgroundService(logger)
{
    /// <summary>The file the Worker's and the all-in-one image's HEALTHCHECK read (/tmp in the images).</summary>
    public static readonly string HeartbeatFile = Path.Combine(Path.GetTempPath(), "garagestack-worker.heartbeat");

    internal string FilePath { get; init; } = HeartbeatFile;

    private bool? _wasHealthy;

    protected override string Name => "Worker heartbeat";

    // The API's health check gives the Worker a minute of silence, four beats.
    protected override TimeSpan Interval => TimeSpan.FromSeconds(15);

    protected override Task RunOnceAsync(CancellationToken ct) => BeatAsync(ct);

    internal async Task BeatAsync(CancellationToken ct)
    {
        var mqttConnected = mqtt.IsConnected;
        var databaseReachable = await TellTheApiAsync(mqttConnected, ct);

        var healthy = mqttConnected && databaseReachable;
        if (healthy)
            await File.WriteAllTextAsync(FilePath, DateTime.UtcNow.ToString("O"), ct);

        // State changes only: a beat every 15 seconds is not news. Not healthy yet at start-up is
        // not news either, since the broker connection is still being made.
        if (_wasHealthy != healthy && (healthy || _wasHealthy == true))
        {
            if (healthy)
                logger.LogInformation("Worker is healthy");
            else
                logger.LogWarning("Worker is unhealthy: broker {Mqtt}, database {Database}",
                    mqttConnected ? "connected" : "disconnected", databaseReachable ? "reachable" : "unreachable");
        }
        _wasHealthy = healthy;
    }

    /// <returns>Whether the database answered, which the notification needs anyway.</returns>
    private async Task<bool> TellTheApiAsync(bool mqttConnected, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Database.CanConnectAsync(ct)) return false;

            await db.Database.NotifyAsync(PgChannels.WorkerHeartbeat, new WorkerHeartbeatPayload(mqttConnected).ToJson(), ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database going away mid-beat; logged once, as a state change.
            return false;
        }
    }
}
