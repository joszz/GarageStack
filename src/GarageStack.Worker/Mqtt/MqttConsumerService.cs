using System.Collections.Concurrent;
using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using GarageStack.Worker.Services;
using MQTTnet;
using MQTTnet.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GarageStack.Worker.Mqtt;

public class MqttConsumerService(
    ILogger<MqttConsumerService> logger,
    IOptions<MqttOptions> options,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender) : BackgroundService
{
    private readonly MqttOptions _options = options.Value;
    // Tracks last known EngineRunning state per VIN to detect start events. The first
    // observation for a VIN only seeds the tracker; it never fires a notification,
    // preventing bogus "engine started" alerts after a deploy or crash.
    internal readonly VinStateTracker<bool> _engineRunningTracker = new();

    // Same cooldown and "engine-start" category PushNotificationCheckService uses for its own
    // (slower, polled) engine-start detection - this path fires immediately on the MQTT event
    // for low-latency delivery, but still needs its own gate: a flapping EngineRunning signal
    // (reconnect/replay noise) would otherwise send an ungated push on every observed
    // false->true transition. The two services' gates are separate instances, but both check
    // AppNotifications via the same category key, so either one seeing a recent send suppresses
    // the other.
    private readonly NotificationCooldownGate _engineStartCooldownGate = new(TimeSpan.FromHours(1));

    // MQTT polling cycles emit several messages within ~2 seconds (one per topic).
    // Messages arriving within this window are merged into the same DB row so that
    // each row represents a complete poll rather than a single field, reducing row
    // count by ~9x and ensuring all chart fields land in the same sample.
    //
    // Unlike _engineRunningTracker, this dictionary is read, awaited on (the DB call in
    // MergeOrAddTelemetryAsync), then written back - a plain lock can't cover that critical
    // section without blocking across an await. Each vehicle gets its own SemaphoreSlim instead
    // (same pattern as VehicleCommandGate), rather than relying on MQTTnet invoking
    // ApplicationMessageReceivedAsync for one message at a time on this client, which is an
    // implementation detail this class shouldn't assume.
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(15);
    internal readonly Dictionary<int, (long RowId, DateTime RecordedAt)> _mergeState = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _mergeGates = new();

    protected virtual IMqttClient CreateMqttClient() => new MqttClientFactory().CreateMqttClient();
    protected virtual TimeSpan RetryDelay => TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = CreateMqttClient();

        client.ApplicationMessageReceivedAsync += msg => HandleMessageAsync(msg, stoppingToken);

        // CleanSession(false) + a stable ClientId let the broker retain a persistent
        // session across reconnects, so it queues messages for us while we're offline.
        // Actual redelivery still depends on the effective QoS being >=1, i.e. it also
        // requires the saic-mqtt-gateway publisher to publish at QoS >=1 - the broker
        // downgrades delivery to the lower of publish/subscribe QoS.
        var mqttOptionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId(_options.ClientId)
            .WithCleanSession(false);

        if (!string.IsNullOrWhiteSpace(_options.Username))
            mqttOptionsBuilder.WithCredentials(_options.Username, _options.Password);

        var mqttOptions = mqttOptionsBuilder.Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            // Completed when the broker drops the connection so the loop can re-enter connect logic.
            var disconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<MqttClientDisconnectedEventArgs, Task> onDisconnected = _ =>
            {
                disconnectedTcs.TrySetResult();
                return Task.CompletedTask;
            };

            client.DisconnectedAsync += onDisconnected;
            try
            {
                await client.ConnectAsync(mqttOptions, stoppingToken);
                logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", _options.Host, _options.Port);

                await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter("saic/#", MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithTopicFilter("homeassistant/#", MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(), stoppingToken);
                logger.LogInformation("Subscribed to saic/# and homeassistant/#");

                // Waits until the broker disconnects or the host is shutting down.
                await disconnectedTcs.Task.WaitAsync(stoppingToken);
                logger.LogWarning("MQTT broker disconnected, will reconnect...");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MQTT connection error, reconnecting in 5s...");
            }
            finally
            {
                client.DisconnectedAsync -= onDisconnected;
                if (client.IsConnected)
                    await client.DisconnectAsync(cancellationToken: CancellationToken.None);
            }

            // Reconnect delay is outside the catch block so OperationCanceledException
            // on shutdown is handled cleanly without nesting exceptions.
            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Resolves (creating on first sight) the vehicle for `vin` within a fresh DI scope. The
    // caller owns disposal of the returned scope and can resolve further scoped services
    // (e.g. ITelemetryRepository, AppDbContext) from the same ServiceProvider before disposing it.
    private async Task<(IServiceScope Scope, IVehicleRepository VehicleRepo, Vehicle Vehicle)> ResolveVehicleInNewScopeAsync(
        string vin, string? saicUser, CancellationToken ct)
    {
        var scope = scopeFactory.CreateScope();
        var vehicleRepo = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();
        var vehicle = await vehicleRepo.GetOrCreateByVinAsync(vin, saicUser, ct);
        return (scope, vehicleRepo, vehicle);
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e, CancellationToken ct)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;

        // Home Assistant discovery payloads carry hw_version inside device JSON - use it for vehicle-type detection
        if (topic.StartsWith("homeassistant/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleHaDiscoveryAsync(payload, ct);
            return;
        }

        if (!MqttTopicParser.TryExtractVin(topic, out var vin))
        {
            logger.LogDebug("Skipping non-vehicle topic: {Topic}", topic);
            return;
        }

        MqttTopicParser.TryExtractUser(topic, out var saicUser);
        var subtopic = MqttTopicParser.ExtractSubtopic(topic);

        // Capability config messages - store as JSON on the vehicle record
        if (subtopic.StartsWith("info/configuration/", StringComparison.OrdinalIgnoreCase))
        {
            var configKey = subtopic["info/configuration/".Length..];
            logger.LogInformation("MQTT config - VIN={Vin} key={Key} payloadBytes={PayloadBytes}", vin, configKey, payload.Length);
            try
            {
                var resolved = await ResolveVehicleInNewScopeAsync(vin, saicUser, ct);
                using var scope = resolved.Scope;
                await resolved.VehicleRepo.SetConfigValueAsync(resolved.Vehicle.Id, configKey, payload, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist config for VIN={Vin} key={Key}", vin, configKey);
            }
            return;
        }

        var patch = new TelemetrySnapshot();
        if (!TelemetryMapper.ApplyMessage(patch, subtopic, payload))
        {
            var ns = subtopic.Split('/')[0];
            if (ns is "info" or "refresh" or "_internal" or "available")
                logger.LogDebug("MQTT metadata (skipped) - VIN={Vin} subtopic={Subtopic}", vin, subtopic);
            else
                logger.LogWarning("Unmapped telemetry topic - VIN={Vin} subtopic={Subtopic} payloadBytes={PayloadBytes}", vin, subtopic, payload.Length);
            return;
        }

        logger.LogDebug("MQTT mapped - VIN={Vin} subtopic={Subtopic} payloadBytes={PayloadBytes}", vin, subtopic, payload.Length);

        try
        {
            var resolved = await ResolveVehicleInNewScopeAsync(vin, saicUser, ct);
            using var scope = resolved.Scope;
            var vehicle = resolved.Vehicle;
            var telemetryRepo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            patch.VehicleId = vehicle.Id;
            patch.RecordedAt = DateTime.UtcNow;

            await MergeOrAddTelemetryAsync(vehicle.Id, patch, topic, telemetryRepo, ct);

            var tripCompleted = await CheckEngineStartAsync(vin, patch, db, ct);
            if (tripCompleted && patch.VehicleId > 0)
            {
                var vid = patch.VehicleId.ToString();
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_notify('trip_completed', {vid})", ct);
                logger.LogInformation("Trip completed for vehicleId={VehicleId} - notifying SignalR clients", patch.VehicleId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist telemetry for VIN={Vin} topic={Topic}", vin, topic);
        }
    }

    // Serializes read-await-write access to _mergeState per vehicle so two concurrently
    // dispatched messages for the same vehicle can't race on which row they merge into.
    internal async Task MergeOrAddTelemetryAsync(
        int vehicleId, TelemetrySnapshot patch, string topic, ITelemetryRepository telemetryRepo, CancellationToken ct)
    {
        var gate = _mergeGates.GetOrAdd(vehicleId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_mergeState.TryGetValue(vehicleId, out var last) &&
                patch.RecordedAt - last.RecordedAt <= MergeWindow)
            {
                await telemetryRepo.MergeIntoAsync(last.RowId, patch, ct);
                _mergeState[vehicleId] = (last.RowId, patch.RecordedAt);
            }
            else
            {
                patch.RawTopic = topic;
                var newId = await telemetryRepo.AddAsync(patch, ct);
                _mergeState[vehicleId] = (newId, patch.RecordedAt);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task<bool> CheckEngineStartAsync(string vin, TelemetrySnapshot snapshot, AppDbContext db, CancellationToken ct)
    {
        if (snapshot.EngineRunning is null) return false;

        var current = snapshot.EngineRunning.Value;
        var hadPrevious = _engineRunningTracker.TryUpdate(vin, current, out var wasRunning);

        // First observation after startup is treated as a no-op to avoid false "engine started"
        // alerts when the worker restarts while driving.
        switch (BoolTransitionDetector.Detect(hadPrevious, wasRunning, current))
        {
            case StateTransition.TurnedOn:
                var shouldNotify = await _engineStartCooldownGate.ShouldNotifyAsync(vin, "engine-start", cutoff =>
                    db.WasNotificationSentSinceAsync("engine-start", snapshot.VehicleId, cutoff, ct));
                if (shouldNotify)
                {
                    logger.LogInformation("Engine started for VIN={Vin} - sending push notification", vin);
                    await pushSender.SendToAllAsync("Engine started", "Your car has been started.", ct, "engine-start", snapshot.VehicleId);
                }
                return false;

            case StateTransition.TurnedOff:
                // A trip just completed
                return true;

            default:
                return false;
        }
    }

    private async Task HandleHaDiscoveryAsync(string payload, CancellationToken ct)
    {
        if (!payload.Contains("hw_version") || !payload.Contains("identifiers"))
            return;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (!root.TryGetProperty("device", out var device)) return;
            if (!device.TryGetProperty("hw_version", out var hwVersionEl)) return;
            if (!device.TryGetProperty("identifiers", out var identifiers)) return;

            var hwVersion = hwVersionEl.GetString();
            if (string.IsNullOrWhiteSpace(hwVersion)) return;

            device.TryGetProperty("model", out var modelEl);
            var model = modelEl.ValueKind == JsonValueKind.String ? modelEl.GetString() : null;

            // VIN is the first string in the identifiers array
            string? vin = null;
            foreach (var id in identifiers.EnumerateArray())
            {
                if (id.ValueKind == JsonValueKind.String)
                {
                    vin = id.GetString();
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(vin)) return;

            logger.LogInformation("HA discovery - VIN={Vin} hw_version={HwVersion} model={Model}", vin, hwVersion, model);

            var resolved = await ResolveVehicleInNewScopeAsync(vin, null, ct);
            using var scope = resolved.Scope;
            await resolved.VehicleRepo.SetConfigValueAsync(resolved.Vehicle.Id, "hw_version", hwVersion, ct);
            if (!string.IsNullOrWhiteSpace(model))
                await resolved.VehicleRepo.SetModelAsync(resolved.Vehicle.Id, model, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse HA discovery payload");
        }
    }
}
