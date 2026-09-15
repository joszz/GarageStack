using System.Text;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace GarageStack.Api.Services;

public class MqttPublisher(IOptions<MqttOptions> options, ILogger<MqttPublisher> logger)
    : IHostedService, IMqttPublisher
{
    private readonly MqttOptions _options = options.Value;
    private IMqttClient? _client;
    private MqttClientOptions? _clientOptions;

    public async Task StartAsync(CancellationToken ct)
    {
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId("garagestack-api")
            .WithCleanSession();

        if (!string.IsNullOrWhiteSpace(_options.Username))
            optionsBuilder.WithCredentials(_options.Username, _options.Password);

        _clientOptions = optionsBuilder.Build();

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        try
        {
            await _client.ConnectAsync(_clientOptions, ct);
            logger.LogInformation("MQTT publisher connected to {Host}:{Port}", _options.Host, _options.Port);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MQTT publisher could not connect at startup - commands will fail until connected");
        }
    }

    public async Task PublishAsync(string topic, string payload, CancellationToken ct = default)
    {
        if (_client is null || _clientOptions is null)
            throw new InvalidOperationException("MQTT broker not reachable");

        if (!_client.IsConnected)
        {
            try { await _client.ConnectAsync(_clientOptions, ct); }
            catch (Exception ex)
            {
                logger.LogError(ex, "MQTT publisher reconnect failed");
                throw new InvalidOperationException("MQTT broker not reachable", ex);
            }
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();

        await _client.PublishAsync(message, ct);

        // Topics carry the MG account email and VIN - redacted before logging at Information
        // level, which persists to 30-day rotating files. The full topic (useful for
        // troubleshooting) is still available at Debug level when DEBUG_LOGS is enabled.
        logger.LogInformation("Published MQTT topic={Topic} payloadBytes={PayloadBytes}",
            LogRedaction.MqttTopic(topic), Encoding.UTF8.GetByteCount(payload));
        logger.LogDebug("Published MQTT full topic={Topic}", topic.ReplaceLineEndings(" "));
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_client?.IsConnected == true)
            await _client.DisconnectAsync(cancellationToken: ct);
    }
}
