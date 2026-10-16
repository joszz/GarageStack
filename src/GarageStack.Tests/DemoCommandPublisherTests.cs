using GarageStack.Api;
using GarageStack.Api.Hubs;
using GarageStack.Api.Services;
using GarageStack.Core.Models;
using GarageStack.Data.Demo;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class DemoCommandPublisherTests
{
    private const string Vin = "DEMOVIN0000012345";

    private static string TopicFor(string command) =>
        $"saic/demo@example.com/vehicles/{Vin}/{Core.Helpers.VehicleCommands.TopicFor(command)}/set";

    private static (DemoCommandPublisher Publisher, DemoTelemetryRepository Car, RecordingHub Hub) Create()
    {
        var car = new DemoTelemetryRepository();
        var hub = new RecordingHub();
        var publisher = new DemoCommandPublisher(
            car, hub, new VehicleCommandGate(), NullLogger<DemoCommandPublisher>.Instance, TimeSpan.Zero);
        return (publisher, car, hub);
    }

    [Fact]
    public async Task ACommand_ChangesTheCar_AndIsAnsweredAsCarriedOut()
    {
        var ct = TestContext.Current.CancellationToken;
        var (publisher, car, hub) = Create();

        await publisher.PublishAsync(TopicFor("lock"), "False", ct);
        var result = await hub.WaitForAsync("commandResult");

        Assert.False((await car.GetMergedLatestAsync(1, ct))!.IsLocked);
        Assert.Equal(new CommandResultMessage("lock", true, null), result);
        var snapshot = Assert.IsType<TelemetrySnapshot>(hub.Sent.Single(s => s.Method == "telemetryUpdated").Arg);
        Assert.False(snapshot.IsLocked);
        Assert.All(hub.Sent, s => Assert.Equal(TelemetryHub.GroupFor(1), s.Group));
    }

    [Fact]
    public async Task ChargingWithoutTheCable_IsRefusedWithAReason_AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (publisher, car, hub) = Create();

        await publisher.PublishAsync(TopicFor("charging"), "true", ct);
        var result = Assert.IsType<CommandResultMessage>(await hub.WaitForAsync("commandResult"));

        Assert.False(result.Success);
        Assert.NotNull(result.Detail);
        Assert.False((await car.GetMergedLatestAsync(1, ct))!.IsCharging);
        Assert.DoesNotContain(hub.Sent, s => s.Method == "telemetryUpdated");
    }

    [Theory]
    [InlineData("climate", "front", nameof(TelemetrySnapshot.ClimateMode), "front")]
    [InlineData("climate-temperature", "24", nameof(TelemetrySnapshot.RemoteTemperature), 24.0)]
    [InlineData("seat-left", "2", nameof(TelemetrySnapshot.HeatedSeatFrontLeft), 2)]
    [InlineData("charge-target", "90", nameof(TelemetrySnapshot.SocTargetPercent), 90)]
    [InlineData("battery-heating", "true", nameof(TelemetrySnapshot.BatteryHeating), true)]
    [InlineData("boot", "false", nameof(TelemetrySnapshot.TrunkOpen), true)]
    public async Task EachCommand_LeavesTheCarAsTheDashboardExpectsIt(string command, string value, string field, object expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var car = new DemoTelemetryRepository();

        Assert.Null(car.ApplyCommand(command, value));

        var snapshot = (await car.GetMergedLatestAsync(1, ct))!;
        var actual = typeof(TelemetrySnapshot).GetProperty(field)!.GetValue(snapshot);
        Assert.Equal(Convert.ToString(expected, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ASchedule_IsStoredAsTheCarReportsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var car = new DemoTelemetryRepository();

        car.ApplyCommand("scheduled-charging", """{"mode":"UNTIL_CONFIGURED_TIME","startTime":"23:00","endTime":"06:30"}""");

        var snapshot = (await car.GetMergedLatestAsync(1, ct))!;
        Assert.Equal(("UNTIL_CONFIGURED_TIME", "23:00", "06:30"),
            (snapshot.ChargingScheduleMode, snapshot.ChargingScheduleStartTime, snapshot.ChargingScheduleEndTime));
    }

    // What the publisher sends to browsers, recorded by group and method.
    private sealed class RecordingHub : IHubContext<TelemetryHub>, IHubClients
    {
        private readonly TaskCompletionSource _resultSent = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(string Group, string Method, object? Arg)> Sent { get; } = [];

        IHubClients IHubContext<TelemetryHub>.Clients => this;
        IGroupManager IHubContext<TelemetryHub>.Groups => throw new NotSupportedException();

        public async Task<object?> WaitForAsync(string method)
        {
            await _resultSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lock (Sent) return Sent.Single(s => s.Method == method).Arg;
        }

        public IClientProxy Group(string groupName) => new Proxy(this, groupName);
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

        private sealed class Proxy(RecordingHub hub, string group) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                lock (hub.Sent) hub.Sent.Add((group, method, args.FirstOrDefault()));
                if (method == "commandResult") hub._resultSent.TrySetResult();
                return Task.CompletedTask;
            }
        }
    }
}
