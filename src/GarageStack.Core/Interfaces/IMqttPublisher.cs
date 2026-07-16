namespace GarageStack.Core.Interfaces;

/// <summary>
/// Publishes a message to the MQTT broker that the SAIC gateway listens on, used by the Api
/// to send remote vehicle commands (lock, climate, etc.) - the gateway picks the message up
/// and relays it to the SAIC cloud API. Demo mode swaps in a no-op implementation since there
/// is no real gateway to publish to.
/// </summary>
public interface IMqttPublisher
{
    Task PublishAsync(string topic, string payload, CancellationToken ct = default);
}
