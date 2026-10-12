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
    // A command and a health check can both find the client disconnected at once; MQTTnet throws
    // when a second connect starts while the first is still running.
    private readonly SemaphoreSlim _connectGate = new(1, 1);

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

    /// <summary>
    /// Connects again when the link dropped, which it otherwise only does when the next command is
    /// sent. The health check calls this too, so a broker that came back is noticed before then.
    /// </summary>
    /// <returns>Whether the publisher is connected.</returns>
    public async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client is null || _clientOptions is null) return false;
        if (_client.IsConnected) return true;

        await _connectGate.WaitAsync(ct);
        try
        {
            if (!_client.IsConnected)
                await _client.ConnectAsync(_clientOptions, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "MQTT publisher reconnect failed");
            return false;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task PublishAsync(string topic, string payload, CancellationToken ct = default)
    {
        if (_client is null || !await EnsureConnectedAsync(ct))
            throw new InvalidOperationException("MQTT broker not reachable");

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
