using GarageStack.Core.Helpers;

namespace GarageStack.Worker.Mqtt;

/// <summary>Stores the capability configuration the gateway publishes per car as JSON on the vehicle record.</summary>
public sealed class VehicleConfigHandler(ILogger logger, VehicleResolver vehicles) : IMqttMessageHandler
{
    private const string Prefix = "info/configuration/";

    public async Task<bool> TryHandleAsync(MqttMessage message, CancellationToken ct)
    {
        if (message.Vehicle is not { } topic || !topic.Subtopic.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var configKey = topic.Subtopic[Prefix.Length..];
        // VIN and account email identify one person; only the Debug lines carry them in full.
        var vinForLog = LogRedaction.Vin(topic.Vin);
        logger.LogInformation("MQTT config - VIN={Vin} key={Key} payloadBytes={PayloadBytes}", vinForLog, configKey, message.Payload.Length);
        try
        {
            using var resolved = await vehicles.ResolveAsync(topic.Vin, topic.User, ct);
            await resolved.Vehicles.SetConfigValueAsync(resolved.VehicleId, configKey, message.Payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist config for VIN={Vin} key={Key}", vinForLog, configKey);
        }

        return true;
    }
}
