namespace GarageStack.Core.Configuration;

/// <summary>
/// Bound from the "Mqtt" configuration section by both the Api (outbound command publishing)
/// and the Worker (telemetry consumption), so the two processes read broker settings the same way.
/// </summary>
public class MqttOptions
{
    public const string SectionName = "Mqtt";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string? Username { get; set; }
    public string? Password { get; set; }

    // Stable across reconnects so the broker can recognize this as the same
    // persistent session (see WithCleanSession(false) in MqttConsumerService).
    public string ClientId { get; set; } = "garagestack-worker";
}
