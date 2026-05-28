using GarageStack.Core.Interfaces;
using MQTTnet;
using System.Text;

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
        if (_client is null || !_client.IsConnected)
        {
            try { await _client!.ConnectAsync(_options!, ct); }
            catch (Exception ex)
            {
                logger.LogError(ex, "MQTT publisher reconnect failed");
                throw new InvalidOperationException("MQTT broker not reachable", ex);
            }
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithRetainFlag(false)
            .Build();

        await _client.PublishAsync(message, ct);
        logger.LogInformation("Published MQTT topic={Topic} payloadBytes={PayloadBytes}", topic, Encoding.UTF8.GetByteCount(payload));
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_client?.IsConnected == true)
            await _client.DisconnectAsync(cancellationToken: ct);
    }
}
