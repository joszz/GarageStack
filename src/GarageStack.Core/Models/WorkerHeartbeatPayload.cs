using System.Text.Json;

namespace GarageStack.Core.Models;

/// <summary>
/// What the Worker reports about itself every few seconds on the worker_heartbeat channel, so the
/// API's health check can speak for the whole stack. The heartbeat arriving at all proves the
/// Worker runs and reaches the database; this adds whether it is subscribed to the broker.
/// </summary>
public sealed record WorkerHeartbeatPayload(bool MqttConnected)
{
    public string ToJson() => PgNotifyJson.Serialize(this);

    public static WorkerHeartbeatPayload? FromJson(string json)
    {
        try
        {
            return PgNotifyJson.Deserialize<WorkerHeartbeatPayload>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
