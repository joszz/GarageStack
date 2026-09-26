namespace GarageStack.Worker.Mqtt;

/// <summary>One MQTT message as the Worker received it, with its topic parsed once.</summary>
public sealed record MqttMessage(string Topic, string Payload, bool Retain)
{
    /// <summary>
    /// The account, VIN and subtopic of a saic/{user}/vehicles/{vin}/... topic, or null for any
    /// other topic (Home Assistant discovery, a malformed VIN).
    /// </summary>
    public ParsedSaicTopic? Vehicle { get; } = MqttTopicParser.TryParse(Topic, out var parsed) ? parsed : null;
}

/// <summary>
/// Deals with one kind of gateway message. <see cref="MqttConsumerService"/> offers every message
/// to its handlers in order, and the first one that claims it is the only one to see it.
/// </summary>
public interface IMqttMessageHandler
{
    /// <returns>True when the message was this handler's, whatever it then did with it.</returns>
    Task<bool> TryHandleAsync(MqttMessage message, CancellationToken ct);
}
