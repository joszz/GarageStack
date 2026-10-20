using System.Collections.Concurrent;
using System.Text.Json;
using GarageStack.Core.Helpers;

namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Reads the gateway's Home Assistant discovery messages for the one thing GarageStack needs from
/// them: the hardware version (and model) of each car, which is how its drivetrain is detected.
/// </summary>
public sealed class HaDiscoveryHandler(ILogger logger, VehicleResolver vehicles) : IMqttMessageHandler
{
    // Per VIN, the hardware version and model last stored. The gateway announces every one of a
    // car's ~100 entities with the same device block, and does so again on each start and each
    // time Home Assistant comes online, so only a change is logged and written.
    private readonly ConcurrentDictionary<string, (string HwVersion, string? Model)> _stored = new();

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
            // Other devices announce themselves under homeassistant/ too (zigbee2mqtt, ESPHome),
            // with identifiers of their own; only a car's VIN may create a vehicle.
            if (!VinFormat.IsValid(vin)) return;
            if (_stored.TryGetValue(vin, out var stored) && stored == (hwVersion, model)) return;

            logger.LogInformation("HA discovery - VIN={Vin} hw_version={HwVersion} model={Model}", LogRedaction.Vin(vin), hwVersion, model);

            using var resolved = await vehicles.ResolveAsync(vin, null, ct);
            await resolved.Vehicles.SetConfigValueAsync(resolved.VehicleId, "hw_version", hwVersion, ct);
            if (!string.IsNullOrWhiteSpace(model))
                await resolved.Vehicles.SetModelAsync(resolved.VehicleId, model, ct);
            // Only once written, so a failed write is tried again on the next announcement.
            _stored[vin] = (hwVersion, model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse HA discovery payload");
        }
    }
}
