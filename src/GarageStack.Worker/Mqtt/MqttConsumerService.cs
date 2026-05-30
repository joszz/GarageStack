using System.Text.Json;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Worker.Services;
using MQTTnet;
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
    // Tracks last known EngineRunning state per VIN to detect start events
    internal readonly Dictionary<string, bool> _lastEngineRunning = new();

    protected virtual IMqttClient CreateMqttClient() => new MqttClientFactory().CreateMqttClient();
    protected virtual TimeSpan RetryDelay => TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = CreateMqttClient();

        client.ApplicationMessageReceivedAsync += msg => HandleMessageAsync(msg, stoppingToken);

        var mqttOptionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithCleanSession();

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
                    .WithTopicFilter("saic/#")
                    .WithTopicFilter("homeassistant/#")
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
            using var cfgScope = scopeFactory.CreateScope();
            var vehicleRepo = cfgScope.ServiceProvider.GetRequiredService<IVehicleRepository>();
            try
            {
                var vehicle = await vehicleRepo.GetOrCreateByVinAsync(vin, saicUser, ct);
                await vehicleRepo.SetConfigValueAsync(vehicle.Id, configKey, payload, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist config for VIN={Vin} key={Key}", vin, configKey);
            }
            return;
        }

        var probe = new TelemetrySnapshot();
        if (!TelemetryMapper.ApplyMessage(probe, subtopic, payload))
        {
            var ns = subtopic.Split('/')[0];
            if (ns is "info" or "refresh" or "_internal" or "available")
                logger.LogDebug("MQTT metadata (skipped) - VIN={Vin} subtopic={Subtopic}", vin, subtopic);
            else
                logger.LogWarning("Unmapped telemetry topic - VIN={Vin} subtopic={Subtopic} payloadBytes={PayloadBytes}", vin, subtopic, payload.Length);
            return;
        }

        logger.LogDebug("MQTT mapped - VIN={Vin} subtopic={Subtopic} payloadBytes={PayloadBytes}", vin, subtopic, payload.Length);

        using var scope = scopeFactory.CreateScope();
        var vehicleRepoMain = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();
        var telemetryRepo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        try
        {
            var vehicle = await vehicleRepoMain.GetOrCreateByVinAsync(vin, saicUser, ct);

            var snapshot = new TelemetrySnapshot
            {
                VehicleId = vehicle.Id,
                RecordedAt = DateTime.UtcNow,
                RawTopic = topic,
                RawPayload = payload
            };

            TelemetryMapper.ApplyMessage(snapshot, subtopic, payload);
            await telemetryRepo.AddAsync(snapshot, ct);

            await CheckEngineStartAsync(vin, snapshot, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist telemetry for VIN={Vin} topic={Topic}", vin, topic);
        }
    }

    internal async Task CheckEngineStartAsync(string vin, TelemetrySnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.EngineRunning is null) return;

        var wasRunning = _lastEngineRunning.TryGetValue(vin, out var prev) && prev;
        _lastEngineRunning[vin] = snapshot.EngineRunning.Value;

        if (snapshot.EngineRunning.Value && !wasRunning)
        {
            logger.LogInformation("Engine started for VIN={Vin} - sending push notification", vin);
            await pushSender.SendToAllAsync("Engine started", "Your car has been started.", ct);
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

            using var scope = scopeFactory.CreateScope();
            var vehicleRepo = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();
            var vehicle = await vehicleRepo.GetOrCreateByVinAsync(vin, null, ct);
            await vehicleRepo.SetConfigValueAsync(vehicle.Id, "hw_version", hwVersion, ct);
            if (!string.IsNullOrWhiteSpace(model))
                await vehicleRepo.SetModelAsync(vehicle.Id, model, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse HA discovery payload");
        }
    }
}
