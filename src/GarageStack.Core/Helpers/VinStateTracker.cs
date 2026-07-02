namespace GarageStack.Core.Helpers;

/// <summary>
/// Tracks the last-observed value of some per-vehicle telemetry field (e.g. EngineRunning, IsCharging)
/// so callers can detect state transitions across MQTT messages / polling cycles.
/// </summary>
public sealed class VinStateTracker<T>
{
    private readonly Dictionary<string, T> _state = new();

    /// <summary>
    /// Records <paramref name="current"/> as the new value for <paramref name="vin"/> and returns the
    /// value seen before this call via <paramref name="previous"/>. Returns false on the first observation
    /// for a given VIN, in which case <paramref name="previous"/> is default(T) and should not be trusted.
    /// </summary>
    public bool TryUpdate(string vin, T current, out T previous)
    {
        var hadPrevious = _state.TryGetValue(vin, out previous!);
        _state[vin] = current;
        return hadPrevious;
    }
}
