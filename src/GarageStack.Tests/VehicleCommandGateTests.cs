using System.Diagnostics;
using GarageStack.Api;

namespace GarageStack.Tests;

public class VehicleCommandGateTests
{
    private const string LockTopic = "doors/locked";
    private const string ClimateTopic = "climate/remoteClimateState";

    // Long enough that a test waiting on it would hit its own 2s timeout first: anything that
    // finishes inside that timeout was released by something other than the hold running out.
    private static readonly TimeSpan LongHold = TimeSpan.FromSeconds(30);

    private static Task Publish() => Task.CompletedTask;

    // Starts a second command for the VIN and reports whether it got through within 2s.
    private static async Task<bool> NextCommandRunsAsync(VehicleCommandGate gate, string vin, CancellationToken ct)
    {
        var ran = false;
        var next = gate.RunAsync(vin, LockTopic, () => { ran = true; return Task.CompletedTask; }, ct);
        var completed = await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(2), ct));
        return completed == next && ran;
    }

    [Fact]
    public async Task RunAsync_InvokesPublish()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new VehicleCommandGate(TimeSpan.FromMilliseconds(10));
        var published = false;

        await gate.RunAsync("VIN1", LockTopic, () => { published = true; return Task.CompletedTask; }, ct);

        Assert.True(published);
    }

    [Fact]
    public async Task RunAsync_SameVin_WaitsForHoldWindowBeforeNextCommand()
    {
        var ct = TestContext.Current.CancellationToken;
        var holdDuration = TimeSpan.FromMilliseconds(150);
        var gate = new VehicleCommandGate(holdDuration);

        // Stopwatch, not DateTime.UtcNow: the system clock ticks about once every 15ms on
        // Windows, so a wait of exactly the hold duration can read back as slightly less.
        // Task.Delay rounds to that same tick and may fire just inside it, hence the slack:
        // the point being tested is that the second command waits for the window, not that the
        // timer is accurate to the millisecond.
        var timerSlack = TimeSpan.FromMilliseconds(16);
        var firstPublishedAt = 0L;
        var secondStartedAt = 0L;

        await gate.RunAsync("VIN1", LockTopic, () =>
        {
            firstPublishedAt = Stopwatch.GetTimestamp();
            return Task.CompletedTask;
        }, ct);

        await gate.RunAsync("VIN1", LockTopic, () =>
        {
            secondStartedAt = Stopwatch.GetTimestamp();
            return Task.CompletedTask;
        }, ct);

        var waited = Stopwatch.GetElapsedTime(firstPublishedAt, secondStartedAt);
        Assert.True(waited >= holdDuration - timerSlack, $"second command ran after {waited.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task RunAsync_DifferentVins_AreNotSerialized()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new VehicleCommandGate(LongHold);

        await gate.RunAsync("VIN1", LockTopic, Publish, ct);

        Assert.True(await NextCommandRunsAsync(gate, "VIN2", ct));
    }

    [Fact]
    public async Task RunAsync_PublishThrows_ReleasesGateImmediatelyAndRethrows()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new VehicleCommandGate(LongHold);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.RunAsync("VIN1", LockTopic, () => throw new InvalidOperationException("boom"), ct));

        Assert.True(await NextCommandRunsAsync(gate, "VIN1", ct));
    }

    [Fact]
    public async Task Complete_AnswerForTheHeldCommand_ReleasesTheGateEarly()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new VehicleCommandGate(LongHold);
        await gate.RunAsync("VIN1", LockTopic, Publish, ct);

        gate.Complete("VIN1", LockTopic);

        Assert.True(await NextCommandRunsAsync(gate, "VIN1", ct));
    }

    // Home Assistant shares the broker, so an answer can belong to a command GarageStack never
    // sent. The gateway is still busy with ours until its own answer arrives.
    [Fact]
    public async Task Complete_AnswerForAnotherCommand_KeepsTheGateHeld()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new VehicleCommandGate(LongHold);
        await gate.RunAsync("VIN1", LockTopic, Publish, ct);

        gate.Complete("VIN1", ClimateTopic);

        Assert.False(await NextCommandRunsAsync(gate, "VIN1", ct));
    }

    [Fact]
    public async Task Complete_AnswerForAnotherVin_KeepsTheGateHeld()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new VehicleCommandGate(LongHold);
        await gate.RunAsync("VIN1", LockTopic, Publish, ct);

        gate.Complete("VIN2", LockTopic);

        Assert.False(await NextCommandRunsAsync(gate, "VIN1", ct));
    }

    // A late answer, arriving after the hold already ran out, must not throw or release anything.
    [Fact]
    public void Complete_NothingHeld_IsANoOp()
    {
        var gate = new VehicleCommandGate(LongHold);

        gate.Complete("VIN1", LockTopic);
    }
}
