using System.Collections.Concurrent;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Mqtt;

/// <summary>What happens to one MG app message event, and why.</summary>
public enum VehicleMessageVerdict
{
    Push,
    // The message already dealt with: the gateway publishes its latest message again on every start.
    Repeat,
    // SAIC's "vehicle started" message, which the engine-start alert already covers.
    VehicleStart,
    // Sent too long ago to be news, e.g. the account's last message on a fresh install.
    TooOld,
}

/// <summary>
/// Pushes the messages the official MG app shows (alarms, reminders) as GarageStack notifications.
///
/// The gateway sends a message as its time and id on retained info/lastMessage topics, then an
/// event with the text. On every start it publishes its latest message again, however old, so the
/// event alone cannot tell a new message from a repeat. The id can: the newest id dealt with is
/// stored on the vehicle, and an event whose id matches it is a repeat. That relies on the id
/// arriving before its event, which is the order the gateway publishes them in; should they ever
/// cross, the event is taken for the previous message and dropped rather than pushed twice.
/// </summary>
public sealed class VehicleMessageHandler(
    ILogger logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender,
    IStringLocalizer<NotificationStrings> strings,
    TimeProvider timeProvider)
{
    // Generous because SAIC's timestamps carry no zone and the gateway takes them for UTC, so a
    // message can look up to half a day older or newer than it is.
    internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    internal const string VehicleStartType = "323";

    private readonly record struct LatestMessage(string? Id, DateTimeOffset? SentAt);

    // Per VIN, the id and time the gateway most recently announced. Written and read by concurrent
    // MQTT dispatches, like MqttConsumerService's own per-vehicle state.
    private readonly ConcurrentDictionary<string, LatestMessage> _latest = new();

    public static VehicleMessageVerdict Judge(
        string messageType, string? messageId, DateTimeOffset? sentAt, string? lastMessageId, DateTimeOffset now)
    {
        if (messageId is not null && messageId == lastMessageId)
            return VehicleMessageVerdict.Repeat;
        if (messageType == VehicleStartType)
            return VehicleMessageVerdict.VehicleStart;
        // An unknown time cannot prove a message old, so it counts as new.
        if (sentAt is { } sent && now - sent > MaxAge)
            return VehicleMessageVerdict.TooOld;
        return VehicleMessageVerdict.Push;
    }

    /// <returns>true when <paramref name="subtopic"/> belongs to MG app messages, whether or not anything was pushed.</returns>
    public async Task<bool> TryHandleAsync(
        string vin, string saicUser, string subtopic, string payload, bool retained, CancellationToken ct)
    {
        switch (subtopic)
        {
            case GatewayVehicleMessage.SentAtSubtopic:
                var sentAt = GatewayVehicleMessage.ParseSentAt(payload);
                _latest.AddOrUpdate(vin, _ => new(null, sentAt), (_, latest) => latest with { SentAt = sentAt });
                return true;

            case GatewayVehicleMessage.IdSubtopic:
                var id = GatewayVehicleMessage.ParseId(payload);
                _latest.AddOrUpdate(vin, _ => new(id, null), (_, latest) => latest with { Id = id });
                // A retained id is one the broker held before this Worker subscribed. Its event was
                // published while nobody listened, so the message is dealt with rather than news:
                // pushing it when the gateway next repeats it would deliver it hours or days late.
                if (retained && id is not null)
                    await RecordAsync(vin, saicUser, id, ct);
                return true;

            case GatewayVehicleMessage.EventSubtopic:
                // The gateway never retains this event; a broker replaying one would replay an old message.
                if (!retained)
                    await HandleEventAsync(vin, saicUser, payload, ct);
                return true;

            default:
                return false;
        }
    }

    private async Task HandleEventAsync(string vin, string saicUser, string payload, CancellationToken ct)
    {
        var vinForLog = LogRedaction.Vin(vin);
        if (!GatewayVehicleMessage.TryParse(payload, out var message))
        {
            logger.LogWarning("Unreadable MG app message for VIN={Vin} payloadBytes={PayloadBytes}", vinForLog, payload.Length);
            return;
        }

        _latest.TryGetValue(vin, out var latest);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var vehicles = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();
            var vehicle = await vehicles.GetOrCreateByVinAsync(vin, saicUser, ct);

            var verdict = Judge(message.MessageType, latest.Id, latest.SentAt, vehicle.LastMessageId, timeProvider.GetUtcNow());
            logger.LogInformation("MG app message for VIN={Vin} type={MessageType} sentAt={SentAt}: {Verdict}",
                vinForLog, message.MessageType, latest.SentAt, verdict);

            if (verdict == VehicleMessageVerdict.Push)
            {
                var title = message.Title.Length > 0 ? message.Title : strings["VehicleMessageTitle"].Value;
                await pushSender.SendToAllAsync(title, message.Content, ct, NotificationCategories.VehicleMessage, vehicle.Id);
            }

            if (latest.Id is not null && verdict != VehicleMessageVerdict.Repeat)
                await vehicles.SetLastMessageIdAsync(vehicle.Id, latest.Id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle MG app message for VIN={Vin}", vinForLog);
        }
    }

    private async Task RecordAsync(string vin, string saicUser, string messageId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var vehicles = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();
            var vehicle = await vehicles.GetOrCreateByVinAsync(vin, saicUser, ct);
            await vehicles.SetLastMessageIdAsync(vehicle.Id, messageId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record the last MG app message for VIN={Vin}", LogRedaction.Vin(vin));
        }
    }
}
