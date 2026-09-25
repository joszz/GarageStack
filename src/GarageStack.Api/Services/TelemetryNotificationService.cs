using System.Collections.Concurrent;
using System.Threading.Channels;
using GarageStack.Api.Hubs;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Extensions;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace GarageStack.Api.Services;

internal sealed class TelemetryNotificationService(
    IConfiguration config,
    IHubContext<TelemetryHub> hubContext,
    IServiceScopeFactory scopeFactory,
    VehicleCommandGate commandGate,
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
            logger.LogWarning("No DefaultConnection configured -- real-time SignalR updates disabled");
            return;
        }

        var channel = Channel.CreateUnbounded<PgEvent>(new UnboundedChannelOptions { SingleReader = true });

        _ = ListenAsync(connectionString, channel.Writer, stoppingToken);

        await foreach (var evt in channel.Reader.ReadAllAsync(stoppingToken))
        {
            switch (evt.Channel)
            {
                case PgChannels.TelemetryUpdated:
                    if (int.TryParse(evt.Payload, out var vehicleId))
                        ScheduleBroadcast(vehicleId, stoppingToken);
                    break;
                case PgChannels.NotificationCreated:
                    _ = BroadcastNotificationAsync(evt.Payload, stoppingToken);
                    break;
                case PgChannels.TripCompleted:
                    if (int.TryParse(evt.Payload, out var tripVehicleId))
                        _ = BroadcastTripCompletedAsync(tripVehicleId, stoppingToken);
                    break;
                case PgChannels.CommandResult:
                    _ = HandleCommandResultAsync(evt.Payload, stoppingToken);
                    break;
            }
        }
    }

    private async Task ListenAsync(string connectionString, ChannelWriter<PgEvent> writer, CancellationToken ct)
    {
        var listenCommand = string.Join("; ", PgChannels.All.Select(c => $"LISTEN {c}"));
        var channelList = string.Join(", ", PgChannels.All);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(ct);

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = listenCommand;
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                logger.LogInformation("Listening for PostgreSQL notifications on {Channels}", channelList);

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
            var notification = NotificationCreatedPayload.FromJson(json);
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

    // The gateway has answered a command: it is free for the next one, and the browser that sent
    // it can stop waiting on telemetry to find out whether it worked.
    private async Task HandleCommandResultAsync(string json, CancellationToken ct)
    {
        try
        {
            var result = CommandResultPayload.FromJson(json);
            if (result is null) return;

            commandGate.Complete(result.Vin, result.Topic);

            var message = CommandResultMessage.From(result);
            if (message is null)
            {
                logger.LogDebug("Command result for {Topic} is not a command the API sends; not broadcast", result.Topic);
                return;
            }

            await hubContext.Clients.Group($"vehicle-{result.VehicleId}")
                .SendAsync("commandResult", message, ct);

            logger.LogDebug("SignalR broadcast commandResult {Command} success={Success} for vehicleId={VehicleId}",
                message.Command, message.Success, result.VehicleId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle command result");
        }
    }
}
