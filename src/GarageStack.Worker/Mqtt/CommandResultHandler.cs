using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;

namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Hands the gateway's answer to a command to the Api, which releases its command gate and tells
/// the browser that sent it.
/// </summary>
public sealed class CommandResultHandler(ILogger logger, VehicleResolver vehicles) : IMqttMessageHandler
{
    public async Task<bool> TryHandleAsync(MqttMessage message, CancellationToken ct)
    {
        if (message.Vehicle is not { } topic || !GatewayCommandResult.TryParse(topic.Subtopic, message.Payload, out var result))
            return false;

        // The gateway publishes results retained, so the broker replays the last one per command
        // on every (re)subscribe. That is an old answer, and forwarding it would show a stale
        // failure or release a later command's gate early.
        if (message.Retain)
            logger.LogDebug("Skipping retained command result - VIN={Vin} subtopic={Subtopic}", topic.Vin, topic.Subtopic);
        else
            await ForwardAsync(topic, result, ct);

        return true;
    }

    private async Task ForwardAsync(ParsedSaicTopic topic, GatewayCommandResult result, CancellationToken ct)
    {
        var vinForLog = LogRedaction.Vin(topic.Vin);
        if (result.Success)
            logger.LogInformation("Command {Topic} succeeded for VIN={Vin}", result.Topic, vinForLog);
        else
            logger.LogWarning("Command {Topic} failed for VIN={Vin}: {Detail}", result.Topic, vinForLog, result.Detail);

        try
        {
            using var resolved = await vehicles.ResolveAsync(topic.Vin, topic.User, ct);
            var db = resolved.Services.GetRequiredService<AppDbContext>();
            var payload = new CommandResultPayload(resolved.VehicleId, topic.Vin, result.Topic, result.Success, result.Detail);
            await db.Database.NotifyAsync(PgChannels.CommandResult, payload.ToJson(), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to forward command result for VIN={Vin} topic={Topic}", vinForLog, result.Topic);
        }
    }
}
