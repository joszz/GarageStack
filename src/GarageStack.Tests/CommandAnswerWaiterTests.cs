using GarageStack.Worker.Mqtt;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class CommandAnswerWaiterTests
{
    private const string Vin = "FAKEVN00000000001";
    private const string ClimateTopic = "climate/remoteClimateState";

    private static readonly TimeSpan Long = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task WaitAsync_GetsTheAnswerOnItsTopic()
    {
        var waiter = new CommandAnswerWaiter();
        using var pending = waiter.Expect(Vin, ClimateTopic);

        waiter.Complete(Vin, new GatewayCommandResult(ClimateTopic, false, "vehicle offline"));
        var answer = await pending.WaitAsync(Long, TestContext.Current.CancellationToken);

        Assert.Equal(new GatewayCommandResult(ClimateTopic, false, "vehicle offline"), answer);
    }

    [Fact]
    public async Task WaitAsync_AnAnswerForAnotherCommandOrCar_IsNotItsAnswer()
    {
        var waiter = new CommandAnswerWaiter();
        using var pending = waiter.Expect(Vin, ClimateTopic);

        waiter.Complete(Vin, new GatewayCommandResult("doors/locked", true, null));
        waiter.Complete("FAKEVN00000000002", new GatewayCommandResult(ClimateTopic, true, null));

        Assert.Null(await pending.WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitAsync_NoAnswerInTime_IsNull()
    {
        using var pending = new CommandAnswerWaiter().Expect(Vin, ClimateTopic);

        Assert.Null(await pending.WaitAsync(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dispose_StopsWaiting_SoALaterExpectGetsTheNextAnswer()
    {
        var waiter = new CommandAnswerWaiter();
        waiter.Expect(Vin, ClimateTopic).Dispose();
        using var next = waiter.Expect(Vin, ClimateTopic);

        waiter.Complete(Vin, new GatewayCommandResult(ClimateTopic, true, null));

        Assert.NotNull(await next.WaitAsync(Long, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommandResultHandler_HandsALiveAnswerToTheWaiter_EvenWhenForwardingItFails()
    {
        var waiter = new CommandAnswerWaiter();
        using var pending = waiter.Expect(Vin, ClimateTopic);
        // The fake scopes resolve no database, so forwarding to the Api fails and is logged.
        var handler = new CommandResultHandler(NullLogger.Instance, new VehicleResolver(new FakeServiceScopeFactory()), waiter);

        var handled = await handler.TryHandleAsync(
            new MqttMessage($"saic/user/vehicles/{Vin}/{ClimateTopic}/result", "Success", Retain: false),
            TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(new GatewayCommandResult(ClimateTopic, true, null), await pending.WaitAsync(Long, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommandResultHandler_ARetainedReplay_IsNotAnAnswer()
    {
        var waiter = new CommandAnswerWaiter();
        using var pending = waiter.Expect(Vin, ClimateTopic);
        var handler = new CommandResultHandler(NullLogger.Instance, new VehicleResolver(new FakeServiceScopeFactory()), waiter);

        await handler.TryHandleAsync(
            new MqttMessage($"saic/user/vehicles/{Vin}/{ClimateTopic}/result", "Failed: old", Retain: true),
            TestContext.Current.CancellationToken);

        Assert.Null(await pending.WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken));
    }
}
