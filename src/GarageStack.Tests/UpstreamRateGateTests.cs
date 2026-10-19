using System.Net;
using System.Net.Http.Headers;
using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class UpstreamRateGateTests
{
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task TryEnter_HoldsTheGateUntilTheEntryIsDisposed()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new UpstreamRateGate();

        using (var first = await gate.TryEnterAsync(Brief, failWhenBackingOff: true, ct))
        {
            Assert.True(first.Entered);

            using var second = await gate.TryEnterAsync(Brief, failWhenBackingOff: true, ct);
            Assert.False(second.Entered);
            Assert.Equal(GateRefusal.Busy, second.Refusal);
        }

        using var third = await gate.TryEnterAsync(Brief, failWhenBackingOff: true, ct);
        Assert.True(third.Entered);
    }

    [Fact]
    public async Task TryEnter_RefusesWhileBackingOff_UnlessTheCallerWaitsItOut()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new UpstreamRateGate();
        gate.SetBackoff(TimeSpan.FromMinutes(1));

        using (var foreground = await gate.TryEnterAsync(Brief, failWhenBackingOff: true, ct))
        {
            Assert.Equal(GateRefusal.BackingOff, foreground.Refusal);
        }

        using var background = await gate.TryEnterAsync(waitAtMost: null, failWhenBackingOff: false, ct);
        Assert.True(background.Entered);
    }

    [Fact]
    public async Task TryEnter_ChecksTheBackoffAgainOnceTheGateIsFree()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new UpstreamRateGate();
        var holder = await gate.TryEnterAsync(Brief, failWhenBackingOff: true, ct);

        // Queued behind a request that gets rate-limited: by the time the gate is free, the
        // backoff is open, and the queued request must not go out.
        var queued = gate.TryEnterAsync(TimeSpan.FromSeconds(5), failWhenBackingOff: true, ct);
        gate.SetBackoff(TimeSpan.FromMinutes(1));
        holder.Dispose();

        using var refused = await queued;
        Assert.Equal(GateRefusal.BackingOff, refused.Refusal);

        // The refused request gave the gate back.
        using var next = await gate.TryEnterAsync(Brief, failWhenBackingOff: false, ct);
        Assert.True(next.Entered);
    }

    [Fact]
    public async Task Dispose_Twice_ReleasesTheGateOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new UpstreamRateGate();
        var entry = await gate.TryEnterAsync(Brief, failWhenBackingOff: true, ct);
        entry.Dispose();
        entry.Dispose();

        // A second release would let two requests in at once.
        using var first = await gate.TryEnterAsync(Brief, failWhenBackingOff: true, ct);
        using var second = await gate.TryEnterAsync(Brief, failWhenBackingOff: true, ct);
        Assert.True(first.Entered);
        Assert.False(second.Entered);
    }

    private static readonly TimeSpan Fallback = TimeSpan.FromSeconds(60);
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BackoffFor_WaitsAsLongAsRetryAfterSaysInSeconds()
    {
        var wait = UpstreamRateGate.BackoffFor(new RetryConditionHeaderValue(TimeSpan.FromSeconds(300)), Fallback, Now);

        Assert.Equal(TimeSpan.FromSeconds(300), wait);
    }

    [Fact]
    public void BackoffFor_WaitsUntilTheDateRetryAfterNames()
    {
        var wait = UpstreamRateGate.BackoffFor(new RetryConditionHeaderValue(Now.AddMinutes(2)), Fallback, Now);

        Assert.Equal(TimeSpan.FromMinutes(2), wait);
    }

    [Fact]
    public void BackoffFor_KeepsTheFallback_WithoutAHeaderOrWithADateAlreadyPast()
    {
        Assert.Equal(Fallback, UpstreamRateGate.BackoffFor(null, Fallback, Now));
        Assert.Equal(Fallback, UpstreamRateGate.BackoffFor(new RetryConditionHeaderValue(Now.AddMinutes(-1)), Fallback, Now));
    }

    [Fact]
    public void BackoffFor_KeepsAnAskedWaitBetweenASecondAndAnHour()
    {
        Assert.Equal(UpstreamRateGate.MinRetryAfter,
            UpstreamRateGate.BackoffFor(new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10)), Fallback, Now));
        Assert.Equal(UpstreamRateGate.MaxRetryAfter,
            UpstreamRateGate.BackoffFor(new RetryConditionHeaderValue(TimeSpan.FromDays(1)), Fallback, Now));
    }

    [Fact]
    public void SetBackoff_FromAResponse_OpensTheWindowItsRetryAfterAsks()
    {
        var gate = new UpstreamRateGate();
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(300));

        var before = DateTimeOffset.UtcNow;
        var wait = gate.SetBackoff(response, Fallback);

        Assert.Equal(TimeSpan.FromSeconds(300), wait);
        Assert.InRange(gate.BackoffUntil, before.AddSeconds(300), DateTimeOffset.UtcNow.AddSeconds(300));
    }
}
