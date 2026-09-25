using System.Collections.Concurrent;

namespace GarageStack.Api;

/// <summary>
/// Serializes vehicle commands per VIN server-side. The real SAIC MQTT gateway only processes
/// one command at a time and takes up to ~30s to reach the car, so two commands published back
/// to back can queue up behind each other at the gateway. The frontend already serializes its
/// own batched sends (ClimateDetailCard.applyAll), this is a backstop against any other caller
/// (a second browser tab, a script against the API, a future frontend regression) doing the same
/// thing unserialized.
/// </summary>
internal sealed class VehicleCommandGate(TimeSpan? holdDuration = null)
{
    // The gateway answers once the SAIC API confirms the command, which it polls for up to 30s
    // (saic-ismart-client-ng's stop_after_delay(30)), so an answer can land a few seconds past
    // that. Matches the frontend's PENDING_TIMEOUT_MS.
    private static readonly TimeSpan DefaultHoldDuration = TimeSpan.FromSeconds(45);

    private readonly TimeSpan _holdDuration = holdDuration ?? DefaultHoldDuration;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<string, HeldCommand> _held = new();

    // The command a VIN's gate is held for, and the signal that the gateway has answered it.
    private sealed record HeldCommand(string Topic, TaskCompletionSource Answered);

    /// <summary>
    /// Waits for any other in-flight command for <paramref name="vin"/> to finish, then runs
    /// <paramref name="publish"/>. On success, holds the gate until the gateway answers the
    /// command (see <see cref="Complete"/>) or the hold duration runs out, without blocking the
    /// caller for that long, so the next command for this VIN doesn't reach the gateway while it is
    /// still busy with this one. On failure (the command never reached the vehicle) releases
    /// immediately so the caller can retry. <paramref name="commandTopic"/> is the command's gateway
    /// topic, which its answer is matched on.
    /// </summary>
    internal async Task RunAsync(string vin, string commandTopic, Func<Task> publish, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(vin, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        var held = new HeldCommand(commandTopic, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        _held[vin] = held;
        try
        {
            await publish();
            _ = ReleaseWhenAnsweredAsync(vin, held, gate);
        }
        catch
        {
            _held.TryRemove(new KeyValuePair<string, HeldCommand>(vin, held));
            gate.Release();
            throw;
        }
    }

    /// <summary>
    /// Releases <paramref name="vin"/>'s gate early: the gateway has answered the command on
    /// <paramref name="commandTopic"/>, so it is free for the next one. An answer for any other
    /// command, such as one Home Assistant sent through the same broker, leaves the gate held.
    /// </summary>
    internal void Complete(string vin, string commandTopic)
    {
        if (_held.TryGetValue(vin, out var held) && held.Topic == commandTopic)
            held.Answered.TrySetResult();
    }

    private async Task ReleaseWhenAnsweredAsync(string vin, HeldCommand held, SemaphoreSlim gate)
    {
        try
        {
            await held.Answered.Task.WaitAsync(_holdDuration);
        }
        catch (TimeoutException)
        {
            // No answer (a gateway too old to send one, or one that never reached the car):
            // the hold duration is the fallback that keeps commands from piling up regardless.
        }

        _held.TryRemove(new KeyValuePair<string, HeldCommand>(vin, held));
        gate.Release();
    }
}
