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
    private readonly TimeSpan _holdDuration = holdDuration ?? TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    /// <summary>
    /// Waits for any other in-flight command for <paramref name="vin"/> to finish, then runs
    /// <paramref name="publish"/>. On success, holds the gate for the configured duration before
    /// releasing it, without blocking the caller for that long, so the next command for this VIN
    /// doesn't reach the gateway until the current one has had time to be processed. On failure
    /// (the command never reached the vehicle) releases immediately so the caller can retry.
    /// </summary>
    internal async Task RunAsync(string vin, Func<Task> publish, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(vin, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await publish();
            _ = Task.Delay(_holdDuration, CancellationToken.None)
                .ContinueWith(_ => gate.Release(), TaskScheduler.Default);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }
}
