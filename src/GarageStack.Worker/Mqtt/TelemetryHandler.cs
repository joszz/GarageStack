using System.Collections.Concurrent;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Stores a telemetry message as a field on the vehicle's current telemetry row, and watches the
/// engine across messages: this is the one place that tells a start from a stop. Last in the
/// consumer's chain, so it takes every vehicle message nothing more specific claimed.
/// </summary>
public sealed class TelemetryHandler(
    ILogger logger,
    VehicleResolver vehicles,
    IPushSender pushSender,
    IStringLocalizer<NotificationStrings> strings) : IMqttMessageHandler
{
    // MQTT polling cycles emit several messages within ~2 seconds (one per topic). Messages
    // arriving within this window are merged into the same DB row so that each row represents a
    // complete poll rather than a single field, reducing row count by ~9x and ensuring all chart
    // fields land in the same sample.
    //
    // This state is read, awaited on (the DB call in MergeOrAddTelemetryAsync), then written back,
    // which a plain lock can't cover without blocking across an await. Each vehicle gets its own
    // SemaphoreSlim instead (same pattern as VehicleCommandGate), rather than relying on MQTTnet
    // invoking ApplicationMessageReceivedAsync for one message at a time, an implementation detail
    // this class shouldn't assume. The dictionary itself is concurrent for the same reason: two
    // different vehicles may write it at once.
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(15);
    internal readonly ConcurrentDictionary<int, (long RowId, DateTime RecordedAt)> _mergeState = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _mergeGates = new();

    // The last known EngineRunning per VIN. The first observation for a VIN only seeds it and never
    // counts as a start or a stop, which keeps a deploy or crash mid-drive from alerting.
    private readonly VinStateTracker<bool> _engineRunning = new();

    // A flapping EngineRunning signal (reconnect or replay noise) would otherwise push on every
    // observed start. The gate also checks AppNotifications, so a restart does not forget a push
    // it just sent.
    private readonly NotificationCooldownGate _engineStartCooldownGate = new(TimeSpan.FromHours(1));

    public async Task<bool> TryHandleAsync(MqttMessage message, CancellationToken ct)
    {
        if (message.Vehicle is not { } topic)
            return false;

        var (saicUser, vin, subtopic) = topic;
        var payload = message.Payload;
        // VIN and account email identify one person; only the Debug lines carry them in full.
        var vinForLog = LogRedaction.Vin(vin);

        var patch = new TelemetrySnapshot();
        if (!TelemetryMapper.ApplyMessage(patch, subtopic, payload))
        {
            var ns = subtopic.Split('/')[0];
            // "command" is the gateway's command/error event, which repeats the failure its
            // {topic}/result already carried.
            if (ns is "info" or "refresh" or "_internal" or "available" or "command")
                logger.LogDebug("MQTT metadata (skipped) - VIN={Vin} subtopic={Subtopic}", vin, subtopic);
            else
                logger.LogWarning("Unmapped telemetry topic - VIN={Vin} subtopic={Subtopic} payloadBytes={PayloadBytes}", vinForLog, subtopic, payload.Length);
            return true;
        }

        logger.LogDebug("MQTT mapped - VIN={Vin} subtopic={Subtopic} payloadBytes={PayloadBytes}", vin, subtopic, payload.Length);

        try
        {
            using var resolved = await vehicles.ResolveAsync(vin, saicUser, ct);
            var vehicleId = resolved.VehicleId;
            var telemetryRepo = resolved.Services.GetRequiredService<ITelemetryRepository>();
            var db = resolved.Services.GetRequiredService<AppDbContext>();

            patch.VehicleId = vehicleId;
            patch.RecordedAt = DateTime.UtcNow;

            await MergeOrAddTelemetryAsync(vehicleId, patch, message.Topic, telemetryRepo, ct);

            if (await ObserveEngineAsync(vin, patch, db, ct) == StateTransition.TurnedOff && vehicleId > 0)
            {
                // The engine stopping ends the trip and parks the car: the Api refreshes the trip
                // list, and the push checks give the driver a grace period before warning about an
                // open door or window.
                await resolved.Vehicles.SetLastParkedAtAsync(vehicleId, patch.RecordedAt, ct);
                await db.Database.NotifyAsync(PgChannels.TripCompleted, vehicleId.ToString(), ct);
                logger.LogInformation("Trip completed for vehicleId={VehicleId} - notifying SignalR clients", vehicleId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist telemetry for VIN={Vin} topic={Topic}", vinForLog, LogRedaction.MqttTopic(message.Topic));
        }

        return true;
    }

    // Serializes read-await-write access to _mergeState per vehicle so two concurrently
    // dispatched messages for the same vehicle can't race on which row they merge into.
    internal async Task MergeOrAddTelemetryAsync(
        int vehicleId, TelemetrySnapshot patch, string topic, ITelemetryRepository telemetryRepo, CancellationToken ct)
    {
        var gate = _mergeGates.GetOrAdd(vehicleId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_mergeState.TryGetValue(vehicleId, out var last) &&
                patch.RecordedAt - last.RecordedAt <= MergeWindow)
            {
                await telemetryRepo.MergeIntoAsync(last.RowId, patch, ct);
                _mergeState[vehicleId] = (last.RowId, patch.RecordedAt);
            }
            else
            {
                patch.RawTopic = topic;
                var newId = await telemetryRepo.AddAsync(patch, ct);
                _mergeState[vehicleId] = (newId, patch.RecordedAt);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Follows the engine across messages. A start sends the engine-start push, once per cooldown.
    /// </summary>
    /// <returns>What the message changed about the engine; a message without an engine reading changes nothing.</returns>
    internal async Task<StateTransition> ObserveEngineAsync(string vin, TelemetrySnapshot snapshot, AppDbContext db, CancellationToken ct)
    {
        if (snapshot.EngineRunning is not { } running)
            return StateTransition.Unchanged;

        var hadPrevious = _engineRunning.TryUpdate(vin, running, out var wasRunning);
        var transition = BoolTransitionDetector.Detect(hadPrevious, wasRunning, running);

        if (transition == StateTransition.TurnedOn)
        {
            var shouldNotify = await _engineStartCooldownGate.ShouldNotifyAsync(vin, NotificationCategories.EngineStart, cutoff =>
                db.WasNotificationSentSinceAsync(NotificationCategories.EngineStart, snapshot.VehicleId, cutoff, ct));
            if (shouldNotify)
            {
                logger.LogInformation("Engine started for VIN={Vin} - sending push notification", LogRedaction.Vin(vin));
                await pushSender.SendToAllAsync(
                    strings["EngineStartTitle"], strings["EngineStartBody"], ct,
                    NotificationCategories.EngineStart, snapshot.VehicleId);
            }
        }

        return transition;
    }
}
