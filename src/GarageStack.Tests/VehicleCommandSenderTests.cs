using GarageStack.Api;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class VehicleCommandSenderTests
{
    private const string Vin = "FAKEVN00000000001";

    private sealed class RecordingPublisher : IMqttPublisher
    {
        public List<(string Topic, string Payload)> Published { get; } = [];

        public Task PublishAsync(string topic, string payload, CancellationToken ct = default)
        {
            Published.Add((topic, payload));
            return Task.CompletedTask;
        }
    }

    private static Vehicle Car(string? saicUser = "driver@example.com") => new() { Id = 1, Vin = Vin, SaicUser = saicUser };

    private static (VehicleCommandSender Sender, RecordingPublisher Publisher) Create()
    {
        var publisher = new RecordingPublisher();
        return (new VehicleCommandSender(publisher, new VehicleCommandGate(TimeSpan.FromMilliseconds(10))), publisher);
    }

    [Fact]
    public async Task SendAsync_KnownCommand_PublishesOnTheGatewaySetTopic()
    {
        var (sender, publisher) = Create();

        var outcome = await sender.SendAsync(Car(), "climate", "on", TestContext.Current.CancellationToken);

        const string expected = "saic/driver@example.com/vehicles/FAKEVN00000000001/climate/remoteClimateState/set";
        Assert.Null(outcome.Refusal);
        Assert.Equal(expected, outcome.Topic);
        Assert.Equal([(expected, "on")], publisher.Published);
    }

    [Fact]
    public async Task SendAsync_AccountNotKnownYet_RefusesWithoutPublishing()
    {
        var (sender, publisher) = Create();

        var outcome = await sender.SendAsync(Car(saicUser: null), "climate", "on", TestContext.Current.CancellationToken);

        Assert.Equal(VehicleCommandSender.AccountUnknownCode, outcome.Refusal?.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task SendAsync_UnknownCommand_RefusesWithoutPublishing()
    {
        var (sender, publisher) = Create();

        var outcome = await sender.SendAsync(Car(), "self-destruct", "now", TestContext.Current.CancellationToken);

        Assert.Equal("command.unknown", outcome.Refusal?.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task SendAsync_ValueTheGatewayRefuses_RefusesWithoutPublishing()
    {
        var (sender, publisher) = Create();

        var outcome = await sender.SendAsync(Car(), "climate-temperature", "40", TestContext.Current.CancellationToken);

        Assert.Equal("command.invalidValue", outcome.Refusal?.Code);
        Assert.Empty(publisher.Published);
    }
}
