namespace GarageStack.Core.Helpers;

/// <summary>
/// Tracks the last-observed value of some per-vehicle telemetry field (e.g. EngineRunning, IsCharging)
/// so callers can detect state transitions across MQTT messages / polling cycles.
/// </summary>
public sealed class VinStateTracker<T>
{
    private readonly Dictionary<string, T> _state = new();

    // Callers hold this as a singleton and invoke TryUpdate from MQTT message handlers; the current
    // handler dispatch happens to be sequential, but that's an MQTTnet implementation detail this
    // class can't rely on, so guard the dictionary explicitly instead of assuming single-threaded access.
    private readonly object _lock = new();

    /// <summary>
    /// Records <paramref name="current"/> as the new value for <paramref name="vin"/> and returns the
    /// value seen before this call via <paramref name="previous"/>. Returns false on the first observation
    /// for a given VIN, in which case <paramref name="previous"/> is default(T) and should not be trusted.
    /// </summary>
    public bool TryUpdate(string vin, T current, out T previous)
    {
        lock (_lock)
        {
            var hadPrevious = _state.TryGetValue(vin, out previous!);
            _state[vin] = current;
            return hadPrevious;
        }
    }
}
