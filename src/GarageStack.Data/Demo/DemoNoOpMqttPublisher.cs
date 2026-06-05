using GarageStack.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Demo;

public sealed class DemoNoOpMqttPublisher : IMqttPublisher
{
    private readonly ILogger<DemoNoOpMqttPublisher> _logger;

    public DemoNoOpMqttPublisher(ILogger<DemoNoOpMqttPublisher> logger) => _logger = logger;

    public Task PublishAsync(string topic, string payload, CancellationToken ct = default)
    {
        var safeTopicForLog = topic.Replace("\r", string.Empty).Replace("\n", string.Empty);
        _logger.LogDebug("Demo mode: suppressed MQTT publish to '{Topic}'", safeTopicForLog);
        return Task.CompletedTask;
    }
}
