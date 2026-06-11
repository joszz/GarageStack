using System.Collections.Concurrent;
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            logger.LogWarning("No DefaultConnection configured — real-time SignalR updates disabled");
            return;
        }

        var channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

        _ = ListenAsync(connectionString, channel.Writer, stoppingToken);

        await foreach (var vehicleId in channel.Reader.ReadAllAsync(stoppingToken))
            ScheduleBroadcast(vehicleId, stoppingToken);
    }

    private async Task ListenAsync(string connectionString, ChannelWriter<int> writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(ct);

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "LISTEN telemetry_updated";
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                logger.LogInformation("Listening for PostgreSQL telemetry_updated notifications");

                conn.Notification += (_, e) =>
                {
                    if (int.TryParse(e.Payload, out var vehicleId))
                        writer.TryWrite(vehicleId);
                };

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
                await BroadcastAsync(vehicleId, stoppingToken);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _pending.TryRemove(new KeyValuePair<int, CancellationTokenSource>(vehicleId, cts));
                cts.Dispose();
            }
        }, cts.Token);
    }

    private async Task BroadcastAsync(int vehicleId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
            var snapshot = await repo.GetMergedLatestAsync(vehicleId, ct);
            if (snapshot is null) return;

            await hubContext.Clients.Group($"vehicle-{vehicleId}")
                .SendAsync("telemetryUpdated", snapshot, ct);

            logger.LogDebug("SignalR broadcast for vehicleId={VehicleId}", vehicleId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast telemetry for vehicleId={VehicleId}", vehicleId);
        }
    }
}
