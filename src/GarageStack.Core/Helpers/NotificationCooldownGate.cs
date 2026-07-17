namespace GarageStack.Core.Helpers;

/// <summary>
/// Deduplicates repeated notifications for the same vehicle+category within a cooldown window.
/// Checks an in-memory cache first (cheap, but reset on restart), then falls back to a
/// caller-supplied DB check so a service restart, or another service's overlapping alert,
/// still suppresses a duplicate.
/// </summary>
public sealed class NotificationCooldownGate(TimeSpan cooldown)
{
    public TimeSpan Cooldown { get; } = cooldown;

    internal readonly Dictionary<string, DateTime> _lastNotified = new();

    // Guards _lastNotified: callers invoke ShouldNotifyAsync from a single BackgroundService
    // loop today, but this class makes no assumption about that staying true.
    private readonly object _lock = new();

    /// <summary>
    /// Returns true if a notification for <paramref name="vin"/>/<paramref name="category"/> should
    /// be sent now. Marks the key as notified as a side effect whenever this returns false because
    /// <paramref name="wasRecentlySentAsync"/> found a match, or true - so callers must send
    /// immediately when this returns true and must not call it speculatively.
    /// </summary>
    public async Task<bool> ShouldNotifyAsync(string vin, string category, Func<Task<bool>> wasRecentlySentAsync)
    {
        var key = $"{vin}/{category}";

        lock (_lock)
        {
            EvictExpired();
            if (_lastNotified.TryGetValue(key, out var last) && DateTime.UtcNow - last < Cooldown)
                return false;
        }

        // Cross-service dedup: check DB so a restart (which clears _lastNotified) or another
        // service's alert for the same category doesn't cause a duplicate notification.
        if (await wasRecentlySentAsync())
        {
            lock (_lock) { _lastNotified[key] = DateTime.UtcNow; }
            return false;
        }

        lock (_lock) { _lastNotified[key] = DateTime.UtcNow; }
        return true;
    }

    // Called with _lock already held. An entry past Cooldown provides no further dedup value
    // (the TryGetValue check above would ignore it anyway), so this is a pure memory-cleanup
    // sweep with no behavioral effect - keeps a long-lived gate (e.g. maintenance items that
    // get deleted and never checked again) from growing forever.
    private void EvictExpired()
    {
        if (_lastNotified.Count == 0) return;

        var now = DateTime.UtcNow;
        List<string>? expired = null;
        foreach (var (key, last) in _lastNotified)
        {
            if (now - last >= Cooldown)
                (expired ??= []).Add(key);
        }
        if (expired is null) return;

        foreach (var key in expired)
            _lastNotified.Remove(key);
    }
}
