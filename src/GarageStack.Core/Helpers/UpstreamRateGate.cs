namespace GarageStack.Core.Helpers;

/// <summary>Why a request did not get to go upstream just now.</summary>
public enum GateRefusal
{
    /// <summary>The request holds the gate.</summary>
    None,

    /// <summary>The upstream asked to slow down, and its backoff window is still open.</summary>
    BackingOff,

    /// <summary>Another request held the gate for longer than this one was willing to wait.</summary>
    Busy,
}

/// <summary>
/// The politeness rules every third-party API client (Open Charge Map, Overpass, Nominatim,
/// Valhalla) needs: one request in flight at a time, a minimum interval between requests, and a
/// backoff window after the upstream answers 429/503/504. A client takes the gate with
/// <see cref="TryEnterAsync"/>, calls <see cref="ThrottleAsync"/> and <see cref="MarkRequestSent"/>
/// around its request, and disposes the entry; the backoff can be read outside the gate so a
/// foreground caller fails fast without waiting.
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

    /// <summary>
    /// Takes the gate for one request. With <paramref name="failWhenBackingOff"/>, an open backoff
    /// window refuses the request, checked before waiting and again once the gate is held, since
    /// the request queued behind may have been rate-limited. <paramref name="waitAtMost"/> bounds
    /// the wait for a caller that has a browser waiting on it; null waits as long as it takes.
    /// </summary>
    /// <returns>
    /// An entry that holds the gate until it is disposed, or, when refused, one that holds nothing
    /// and says why in <see cref="GateEntry.Refusal"/>.
    /// </returns>
    public async Task<GateEntry> TryEnterAsync(TimeSpan? waitAtMost, bool failWhenBackingOff, CancellationToken ct)
    {
        if (failWhenBackingOff && IsBackingOff)
            return GateEntry.Refused(GateRefusal.BackingOff);

        if (waitAtMost is { } timeout)
        {
            if (!await _gate.WaitAsync(timeout, ct))
                return GateEntry.Refused(GateRefusal.Busy);
        }
        else
        {
            await _gate.WaitAsync(ct);
        }

        if (failWhenBackingOff && IsBackingOff)
        {
            _gate.Release();
            return GateEntry.Refused(GateRefusal.BackingOff);
        }

        return new GateEntry(_gate);
    }

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

/// <summary>
/// One request's hold on an <see cref="UpstreamRateGate"/>: released when disposed. A refused
/// entry holds nothing, so disposing it is always safe.
/// </summary>
public sealed class GateEntry : IDisposable
{
    private SemaphoreSlim? _held;

    internal GateEntry(SemaphoreSlim held) => _held = held;

    private GateEntry(GateRefusal refusal) => Refusal = refusal;

    internal static GateEntry Refused(GateRefusal refusal) => new(refusal);

    /// <summary>Why the request may not go out now, or <see cref="GateRefusal.None"/> when it holds the gate.</summary>
    public GateRefusal Refusal { get; }

    public bool Entered => Refusal == GateRefusal.None;

    public void Dispose()
    {
        _held?.Release();
        _held = null;
    }
}
