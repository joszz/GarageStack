using System.Net.Http.Headers;

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
/// Valhalla, the RDW) needs: one request in flight at a time, a minimum interval between requests, and a
/// backoff window after the upstream answers 429/503/504, as long as its Retry-After asks. A client takes the gate with
/// <see cref="TryEnterAsync"/>, sends its request through <see cref="SendAsync"/>, which keeps the
/// interval and reads the upstream's answer for a "slow down", and disposes the entry; the backoff can be read outside the gate so a
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
    /// Call while holding the gate. Sends one request: waits until <paramref name="minInterval"/>
    /// has passed since the previous one and, with <paramref name="honourBackoff"/>, until any
    /// backoff window has closed, then sends it. When the upstream answers that it is busy
    /// (<see cref="IsThrottlingStatus"/>), opens the backoff window its Retry-After asks for, or
    /// <paramref name="fallbackBackoff"/> without one, and says how long in the answer.
    /// </summary>
    public async Task<UpstreamAnswer> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        TimeSpan minInterval,
        bool honourBackoff,
        TimeSpan fallbackBackoff,
        CancellationToken ct)
    {
        await ThrottleAsync(minInterval, honourBackoff, ct);
        _lastRequestAt = DateTimeOffset.UtcNow;
        var response = await send(ct);
        var backoff = IsThrottlingStatus((int)response.StatusCode)
            ? SetBackoff(response, fallbackBackoff)
            : (TimeSpan?)null;
        return new UpstreamAnswer(response, backoff);
    }

    // Delays until minInterval has passed since the previous request and, with honourBackoff,
    // until any active backoff window has ended.
    private async Task ThrottleAsync(TimeSpan minInterval, bool honourBackoff, CancellationToken ct)
    {
        var nextAllowed = _lastRequestAt + minInterval;
        if (honourBackoff && BackoffUntil > nextAllowed)
            nextAllowed = BackoffUntil;

        var wait = nextAllowed - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct);
    }

    // The bounds on a Retry-After an upstream sends: a second at the least, so a "0" cannot
    // turn a refusal into an immediate retry, and an hour at the most, so a mistaken header
    // cannot switch a feature off for the day.
    internal static readonly TimeSpan MinRetryAfter = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    public void SetBackoff(TimeSpan duration) =>
        Interlocked.Exchange(ref _backoffUntilTicks, (DateTimeOffset.UtcNow + duration).UtcTicks);

    /// <summary>
    /// Opens the backoff window an upstream's "slow down" answer asks for: as long as its
    /// Retry-After header says, or <paramref name="fallback"/> when it sends none.
    /// </summary>
    /// <returns>How long the window is, for the log line.</returns>
    internal TimeSpan SetBackoff(HttpResponseMessage response, TimeSpan fallback)
    {
        var duration = BackoffFor(response.Headers.RetryAfter, fallback, DateTimeOffset.UtcNow);
        SetBackoff(duration);
        return duration;
    }

    /// <summary>
    /// The wait a Retry-After header asks for, in seconds or as a date, kept between
    /// <see cref="MinRetryAfter"/> and <see cref="MaxRetryAfter"/>. A missing header, or a date
    /// already past, leaves the caller's own <paramref name="fallback"/>.
    /// </summary>
    internal static TimeSpan BackoffFor(RetryConditionHeaderValue? retryAfter, TimeSpan fallback, DateTimeOffset now)
    {
        var asked = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : null);
        if (asked is not { } wait || wait <= TimeSpan.Zero) return fallback;
        if (wait < MinRetryAfter) return MinRetryAfter;
        return wait > MaxRetryAfter ? MaxRetryAfter : wait;
    }

    /// <summary>True for the upstream status codes that mean "slow down", not "your request is wrong".</summary>
    private static bool IsThrottlingStatus(int statusCode) => statusCode is 429 or 503 or 504;
}

/// <summary>
/// What the upstream answered one request through <see cref="UpstreamRateGate.SendAsync"/>, and
/// whether that answer asked to slow down: <see cref="Backoff"/> is then how long the gate now
/// waits. Disposing it disposes the response.
/// </summary>
public sealed class UpstreamAnswer(HttpResponseMessage response, TimeSpan? backoff) : IDisposable
{
    public HttpResponseMessage Response { get; } = response;

    /// <summary>How long the gate backs off, when the upstream said it was busy; null otherwise.</summary>
    public TimeSpan? Backoff { get; } = backoff;

    public void Dispose() => Response.Dispose();
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
