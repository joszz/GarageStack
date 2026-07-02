namespace GarageStack.Worker.Mqtt;

public class MqttOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string? Username { get; set; }
    public string? Password { get; set; }

    // Stable across reconnects so the broker can recognize this as the same
    // persistent session (see WithCleanSession(false) in MqttConsumerService).
    public string ClientId { get; set; } = "garagestack-worker";
}
