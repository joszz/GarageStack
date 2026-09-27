using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Holds the Worker's MQTT connection and hands every message to the first handler that claims
/// it: Home Assistant discovery, capability config, MG app messages, command results, and last
/// the telemetry itself. Reconnects for as long as the Worker runs.
/// </summary>
public class MqttConsumerService : BackgroundService
{
    private readonly ILogger<MqttConsumerService> _logger;
    private readonly MqttOptions _options;
    private readonly IReadOnlyList<IMqttMessageHandler> _handlers;

    public MqttConsumerService(
        ILogger<MqttConsumerService> logger,
        IOptions<MqttOptions> options,
        IServiceScopeFactory scopeFactory,
        IPushSender pushSender,
        IStringLocalizer<NotificationStrings> strings)
    {
        _logger = logger;
        _options = options.Value;

        var vehicles = new VehicleResolver(scopeFactory);
        _handlers =
        [
            new HaDiscoveryHandler(logger, vehicles),
            new VehicleConfigHandler(logger, vehicles),
            new VehicleMessageHandler(logger, scopeFactory, pushSender, strings, TimeProvider.System),
            new CommandResultHandler(logger, vehicles),
            new TelemetryHandler(logger, vehicles, pushSender, strings),
        ];
    }

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
                _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", _options.Host, _options.Port);

                await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter("saic/#", MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithTopicFilter("homeassistant/#", MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(), stoppingToken);
                _logger.LogInformation("Subscribed to saic/# and homeassistant/#");

                // Waits until the broker disconnects or the host is shutting down.
                await disconnectedTcs.Task.WaitAsync(stoppingToken);
                _logger.LogWarning("MQTT broker disconnected, will reconnect...");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT connection error, reconnecting in 5s...");
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
        var message = new MqttMessage(
            e.ApplicationMessage.Topic,
            e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty,
            e.ApplicationMessage.Retain);

        foreach (var handler in _handlers)
        {
            if (await handler.TryHandleAsync(message, ct))
                return;
        }

        _logger.LogDebug("Skipping non-vehicle topic: {Topic}", message.Topic);
    }
}
