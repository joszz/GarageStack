namespace GarageStack.Core.Helpers;

/// <summary>
/// The politeness rules every third-party map API client (Open Charge Map, Overpass) needs:
/// one request in flight at a time, a minimum interval between requests, and a backoff window
/// after the upstream answers 429/503/504. Callers acquire the gate, call
/// <see cref="ThrottleAsync"/>, send their request, and <see cref="MarkRequestSent"/>; the
/// backoff can be read outside the gate so a foreground caller can fail fast without waiting.
/// </summary>
public sealed class UpstreamRateGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    // Written inside the gate, read outside it (fast-fail pre-checks). Interlocked keeps the
    // cross-thread read consistent without taking the gate.
    private long _backoffUntilTicks = DateTimeOffset.MinValue.UtcTicks;

    public DateTimeOffset BackoffUntil => new(Interlocked.Read(ref _backoffUntilTicks), TimeSpan.Zero);

    public bool IsBackingOff => BackoffUntil > DateTimeOffset.UtcNow;

    /// <summary>Waits for exclusive access; the Worker's background paths use this form.</summary>
    public Task WaitAsync(CancellationToken ct) => _gate.WaitAsync(ct);

    /// <summary>Waits at most <paramref name="timeout"/>; returns false when the gate stays busy.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _gate.WaitAsync(timeout, ct);

    public void Release() => _gate.Release();

    /// <summary>
    /// Call while holding the gate. Delays until <paramref name="minInterval"/> has passed since
    /// the previous request and, when <paramref name="honourBackoff"/> is set, until any active
    /// backoff window has ended.
    /// </summary>
    public async Task ThrottleAsync(TimeSpan minInterval, bool honourBackoff, CancellationToken ct)
    {
        var nextAllowed = _lastRequestAt + minInterval;
        if (honourBackoff && BackoffUntil > nextAllowed)
            nextAllowed = BackoffUntil;

        var wait = nextAllowed - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct);
    }

    /// <summary>Call while holding the gate, right before the HTTP request goes out.</summary>
    public void MarkRequestSent() => _lastRequestAt = DateTimeOffset.UtcNow;

    public void SetBackoff(TimeSpan duration) =>
        Interlocked.Exchange(ref _backoffUntilTicks, (DateTimeOffset.UtcNow + duration).UtcTicks);

    /// <summary>True for the upstream status codes that mean "slow down", not "your request is wrong".</summary>
    public static bool IsThrottlingStatus(int statusCode) => statusCode is 429 or 503 or 504;
}
