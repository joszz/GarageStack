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
}
