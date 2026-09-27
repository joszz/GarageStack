using System.Text.Json;
using GarageStack.Core.Helpers;

namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Reads the gateway's Home Assistant discovery messages for the one thing GarageStack needs from
/// them: the hardware version (and model) of each car, which is how its drivetrain is detected.
/// </summary>
public sealed class HaDiscoveryHandler(ILogger logger, VehicleResolver vehicles) : IMqttMessageHandler
{
    public async Task<bool> TryHandleAsync(MqttMessage message, CancellationToken ct)
    {
        if (!message.Topic.StartsWith("homeassistant/", StringComparison.OrdinalIgnoreCase))
            return false;

        await HandleAsync(message.Payload, ct);
        return true;
    }

    private async Task HandleAsync(string payload, CancellationToken ct)
    {
        if (!payload.Contains("hw_version") || !payload.Contains("identifiers"))
            return;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (!root.TryGetProperty("device", out var device)) return;
            if (!device.TryGetProperty("hw_version", out var hwVersionEl)) return;
            if (!device.TryGetProperty("identifiers", out var identifiers)) return;

            var hwVersion = hwVersionEl.GetString();
            if (string.IsNullOrWhiteSpace(hwVersion)) return;

            device.TryGetProperty("model", out var modelEl);
            var model = modelEl.ValueKind == JsonValueKind.String ? modelEl.GetString() : null;

            // VIN is the first string in the identifiers array
            string? vin = null;
            foreach (var id in identifiers.EnumerateArray())
            {
                if (id.ValueKind == JsonValueKind.String)
                {
                    vin = id.GetString();
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(vin)) return;

            logger.LogInformation("HA discovery - VIN={Vin} hw_version={HwVersion} model={Model}", LogRedaction.Vin(vin), hwVersion, model);

            using var resolved = await vehicles.ResolveAsync(vin, null, ct);
            await resolved.Vehicles.SetConfigValueAsync(resolved.VehicleId, "hw_version", hwVersion, ct);
            if (!string.IsNullOrWhiteSpace(model))
                await resolved.Vehicles.SetModelAsync(resolved.VehicleId, model, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse HA discovery payload");
        }
    }
}
