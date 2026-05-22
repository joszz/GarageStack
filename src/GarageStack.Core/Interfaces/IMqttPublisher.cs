namespace GarageStack.Core.Interfaces;

public interface IMqttPublisher
{
    Task PublishAsync(string topic, string payload, CancellationToken ct = default);
}
