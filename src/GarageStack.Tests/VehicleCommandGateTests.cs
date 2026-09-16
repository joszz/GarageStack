using System.Diagnostics;
using GarageStack.Api;

namespace GarageStack.Tests;

public class VehicleCommandGateTests
{
    [Fact]
    public async Task RunAsync_InvokesPublish()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new VehicleCommandGate(TimeSpan.FromMilliseconds(10));
        var published = false;

        await gate.RunAsync("VIN1", () => { published = true; return Task.CompletedTask; }, ct);

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

        await gate.RunAsync("VIN1", () =>
        {
            firstPublishedAt = Stopwatch.GetTimestamp();
            return Task.CompletedTask;
        }, ct);

        await gate.RunAsync("VIN1", () =>
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
        // Deliberately long hold: if VIN2 incorrectly shared VIN1's gate, the second RunAsync
        // below would still be waiting when the 2s timeout below elapses.
        var gate = new VehicleCommandGate(TimeSpan.FromSeconds(30));

        await gate.RunAsync("VIN1", () => Task.CompletedTask, ct);

        var vin2Ran = false;
        var vin2Task = gate.RunAsync("VIN2", () => { vin2Ran = true; return Task.CompletedTask; }, ct);
        var completed = await Task.WhenAny(vin2Task, Task.Delay(TimeSpan.FromSeconds(2), ct));

        Assert.Same(vin2Task, completed);
        Assert.True(vin2Ran);
    }

    [Fact]
    public async Task RunAsync_PublishThrows_ReleasesGateImmediatelyAndRethrows()
    {
        var ct = TestContext.Current.CancellationToken;
        // Deliberately long hold: if a failed publish still held the gate, the second RunAsync
        // below would still be waiting when the 2s timeout below elapses.
        var gate = new VehicleCommandGate(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.RunAsync("VIN1", () => throw new InvalidOperationException("boom"), ct));

        var secondRan = false;
        var secondTask = gate.RunAsync("VIN1", () => { secondRan = true; return Task.CompletedTask; }, ct);
        var completed = await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(2), ct));

        Assert.Same(secondTask, completed);
        Assert.True(secondRan);
    }
}
