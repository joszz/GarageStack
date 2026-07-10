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

        var firstPublishedAt = DateTime.MinValue;
        var secondStartedAt = DateTime.MinValue;

        await gate.RunAsync("VIN1", () =>
        {
            firstPublishedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        }, ct);

        await gate.RunAsync("VIN1", () =>
        {
            secondStartedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        }, ct);

        Assert.True(secondStartedAt - firstPublishedAt >= holdDuration);
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
