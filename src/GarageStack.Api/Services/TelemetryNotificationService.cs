using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using GarageStack.Api.Hubs;
using GarageStack.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace GarageStack.Api.Services;

public class TelemetryNotificationService(
    IConfiguration config,
    IHubContext<TelemetryHub> hubContext,
    IServiceScopeFactory scopeFactory,
    ILogger<TelemetryNotificationService> logger) : BackgroundService
{
    // Trailing-edge debounce per vehicle: coalesces rapid MQTT bursts (multiple
    // topics per poll cycle) into a single SignalR broadcast after a quiet period.
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _pending = new();

    private record PgEvent(string Channel, string Payload);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            logger.LogWarning("No DefaultConnection configured — real-time SignalR updates disabled");
            return;
        }

        var channel = Channel.CreateUnbounded<PgEvent>(new UnboundedChannelOptions { SingleReader = true });

        _ = ListenAsync(connectionString, channel.Writer, stoppingToken);

        await foreach (var evt in channel.Reader.ReadAllAsync(stoppingToken))
        {
            switch (evt.Channel)
            {
                case "telemetry_updated":
                    if (int.TryParse(evt.Payload, out var vehicleId))
                        ScheduleBroadcast(vehicleId, stoppingToken);
                    break;
                case "notification_created":
                    _ = BroadcastNotificationAsync(evt.Payload, stoppingToken);
                    break;
                case "trip_completed":
                    if (int.TryParse(evt.Payload, out var tripVehicleId))
                        _ = BroadcastTripCompletedAsync(tripVehicleId, stoppingToken);
                    break;
            }
        }
    }

    private async Task ListenAsync(string connectionString, ChannelWriter<PgEvent> writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(ct);

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "LISTEN telemetry_updated; LISTEN notification_created; LISTEN trip_completed";
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                logger.LogInformation("Listening for PostgreSQL notifications on telemetry_updated, notification_created, trip_completed");

                conn.Notification += (_, e) => writer.TryWrite(new PgEvent(e.Channel, e.Payload));

                while (!ct.IsCancellationRequested)
                    await conn.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PostgreSQL LISTEN connection failed, retrying in 5s");
                try { await Task.Delay(5_000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void ScheduleBroadcast(int vehicleId, CancellationToken stoppingToken)
    {
        if (_pending.TryRemove(vehicleId, out var old))
        {
            old.Cancel();
            old.Dispose();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _pending[vehicleId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2_000, cts.Token);
                await BroadcastTelemetryAsync(vehicleId, stoppingToken);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _pending.TryRemove(new KeyValuePair<int, CancellationTokenSource>(vehicleId, cts));
                cts.Dispose();
            }
        }, cts.Token);
    }

    private async Task BroadcastTelemetryAsync(int vehicleId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
            var snapshot = await repo.GetMergedLatestAsync(vehicleId, ct);
            if (snapshot is null) return;

            await hubContext.Clients.Group($"vehicle-{vehicleId}")
                .SendAsync("telemetryUpdated", snapshot, ct);

            logger.LogDebug("SignalR broadcast telemetryUpdated for vehicleId={VehicleId}", vehicleId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast telemetry for vehicleId={VehicleId}", vehicleId);
        }
    }

    private async Task BroadcastNotificationAsync(string json, CancellationToken ct)
    {
        try
        {
            var notification = JsonSerializer.Deserialize<NotificationPayload>(json, JsonOptions);
            if (notification is null) return;

            await hubContext.Clients.All.SendAsync("notificationReceived", notification, ct);
            logger.LogDebug("SignalR broadcast notificationReceived id={Id}", notification.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast notification");
        }
    }

    private async Task BroadcastTripCompletedAsync(int vehicleId, CancellationToken ct)
    {
        try
        {
            await hubContext.Clients.Group($"vehicle-{vehicleId}")
                .SendAsync("tripCompleted", vehicleId, ct);

            logger.LogDebug("SignalR broadcast tripCompleted for vehicleId={VehicleId}", vehicleId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast tripCompleted for vehicleId={VehicleId}", vehicleId);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Mirrors the payload shape written by PushSenderService
    private sealed record NotificationPayload(
        int Id,
        string Title,
        string Body,
        string CreatedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? VehicleId);
}
