using GarageStack.Api;
using GarageStack.Api.Hubs;
using GarageStack.Api.Services;
using GarageStack.Core.Models;
using GarageStack.Data.Demo;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class DemoCommandPublisherTests
{
    private static readonly Vehicle First = DemoVehicleRepository.DemoVehicle;
    private static readonly Vehicle Second = DemoVehicleRepository.SecondDemoVehicle;

    private static string TopicFor(string command, string? vin = null) =>
        $"saic/demo@example.com/vehicles/{vin ?? First.Vin}/{Core.Helpers.VehicleCommands.TopicFor(command)}/set";

    private static (DemoCommandPublisher Publisher, DemoTelemetryRepository Car, RecordingHub Hub) Create()
    {
        var car = new DemoTelemetryRepository();
        var hub = new RecordingHub();
        var publisher = new DemoCommandPublisher(
            car, new DemoOptions(SecondVehicle: true), hub, new VehicleCommandGate(),
            NullLogger<DemoCommandPublisher>.Instance, TimeSpan.Zero);
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

    // The second car's command is carried out on that car and answered to those watching it; the
    // first car, and whoever watches it, hear nothing.
    [Fact]
    public async Task ACommandForTheSecondCar_ChangesThatCarOnly_AndIsAnsweredToItsViewers()
    {
        var ct = TestContext.Current.CancellationToken;
        var (publisher, car, hub) = Create();

        await publisher.PublishAsync(TopicFor("lock", Second.Vin), "False", ct);
        await hub.WaitForAsync("commandResult");

        Assert.False((await car.GetMergedLatestAsync(Second.Id, ct))!.IsLocked);
        Assert.True((await car.GetMergedLatestAsync(First.Id, ct))!.IsLocked);
        Assert.All(hub.Sent, s => Assert.Equal(TelemetryHub.GroupFor(Second.Id), s.Group));
        var snapshot = Assert.IsType<TelemetrySnapshot>(hub.Sent.Single(s => s.Method == "telemetryUpdated").Arg);
        Assert.Equal(Second.Id, snapshot.VehicleId);
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
    [InlineData("power-tailgate", "open", nameof(TelemetrySnapshot.TrunkOpen), true)]
    public async Task EachCommand_LeavesTheCarAsTheDashboardExpectsIt(string command, string value, string field, object expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var car = new DemoTelemetryRepository();

        Assert.Null(car.ApplyCommand(1, command, value));

        var snapshot = (await car.GetMergedLatestAsync(1, ct))!;
        var actual = typeof(TelemetrySnapshot).GetProperty(field)!.GetValue(snapshot);
        Assert.Equal(Convert.ToString(expected, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task APowerTailgate_ClosesAgainAfterOpening()
    {
        var ct = TestContext.Current.CancellationToken;
        var car = new DemoTelemetryRepository();

        car.ApplyCommand(1, "power-tailgate", "open");
        car.ApplyCommand(1, "power-tailgate", "close");

        Assert.False((await car.GetMergedLatestAsync(1, ct))!.TrunkOpen);
    }

    [Fact]
    public async Task ASchedule_IsStoredAsTheCarReportsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var car = new DemoTelemetryRepository();

        car.ApplyCommand(1, "scheduled-charging", """{"mode":"UNTIL_CONFIGURED_TIME","startTime":"23:00","endTime":"06:30"}""");

        var snapshot = (await car.GetMergedLatestAsync(1, ct))!;
        Assert.Equal(("UNTIL_CONFIGURED_TIME", "23:00", "06:30"),
            (snapshot.ChargingScheduleMode, snapshot.ChargingScheduleStartTime, snapshot.ChargingScheduleEndTime));
    }
}
