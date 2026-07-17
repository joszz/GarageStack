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

    private readonly Dictionary<string, DateTime> _lastNotified = new();

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

    /// <summary>
    /// Convenience overload: computes the cutoff from <see cref="Cooldown"/> and passes it to
    /// <paramref name="wasRecentlySentSinceAsync"/>, so callers only need to supply the DB
    /// check itself (e.g. <c>AppDbContext.WasNotificationSentSinceAsync</c>) instead of also
    /// managing the cutoff calculation at every call site.
    /// </summary>
    public Task<bool> ShouldNotifyAsync(string vin, string category, Func<DateTime, Task<bool>> wasRecentlySentSinceAsync) =>
        ShouldNotifyAsync(vin, category, () => wasRecentlySentSinceAsync(DateTime.UtcNow - Cooldown));
}
