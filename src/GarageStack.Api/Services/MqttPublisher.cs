using GarageStack.Core.Interfaces;
using MQTTnet;
using System.Text;
using System.Text.RegularExpressions;

namespace GarageStack.Api.Services;

public class MqttPublisher(IConfiguration config, ILogger<MqttPublisher> logger)
    : IHostedService, IMqttPublisher
{
    private IMqttClient? _client;
    private MqttClientOptions? _options;

    public async Task StartAsync(CancellationToken ct)
    {
        var host = config["Mqtt:Host"] ?? "localhost";
        var port = int.Parse(config["Mqtt:Port"] ?? "1883");
        var username = config["Mqtt:Username"];
        var password = config["Mqtt:Password"];

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithClientId("garagestack-api")
            .WithCleanSession();

        if (!string.IsNullOrWhiteSpace(username))
            optionsBuilder.WithCredentials(username, password);

        _options = optionsBuilder.Build();

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        try
        {
            await _client.ConnectAsync(_options, ct);
            logger.LogInformation("MQTT publisher connected to {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MQTT publisher could not connect at startup - commands will fail until connected");
        }
    }

    public async Task PublishAsync(string topic, string payload, CancellationToken ct = default)
    {
        if (_client is null || _options is null)
            throw new InvalidOperationException("MQTT broker not reachable");

        if (!_client.IsConnected)
        {
            try { await _client.ConnectAsync(_options, ct); }
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

        var sanitizedTopic = topic.ReplaceLineEndings(" ");
        // Topics carry the MG account email and VIN (saic/{email}/vehicles/{vin}/...) - redact
        // both before logging at Information level, which persists to 30-day rotating files.
        // The full topic (including the command path, useful for troubleshooting) is still
        // available at Debug level when DEBUG_LOGS is enabled.
        var redactedTopic = Regex.Replace(sanitizedTopic, @"^saic/[^/]+/vehicles/[^/]+/", "saic/***/vehicles/***/");
        logger.LogInformation("Published MQTT topic={Topic} payloadBytes={PayloadBytes}", redactedTopic, Encoding.UTF8.GetByteCount(payload));
        logger.LogDebug("Published MQTT full topic={Topic}", sanitizedTopic);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_client?.IsConnected == true)
            await _client.DisconnectAsync(cancellationToken: ct);
    }
}
