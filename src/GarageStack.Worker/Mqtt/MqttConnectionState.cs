namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Whether the Worker is connected and subscribed to the broker right now. Set by
/// <see cref="MqttConsumerService"/>, read by the Worker's heartbeat.
/// </summary>
public sealed class MqttConnectionState
{
    private volatile bool _connected;

    public bool IsConnected => _connected;

    internal void Set(bool connected) => _connected = connected;
}
