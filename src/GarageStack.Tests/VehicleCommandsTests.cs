using System.Text.Json;
using GarageStack.Api.Hubs;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class VehicleCommandsTests
{
    public static TheoryData<string> Commands() =>
    [
        "climate", "climate-temperature", "rear-defroster", "seat-left", "seat-right",
        "find-my-car", "charge-limit", "charge-target", "charging", "charging-cable-lock",
        "battery-heating", "scheduled-charging", "battery-heating-schedule", "lock", "boot", "refresh",
    ];

    // An answer is matched back to its command by topic, so two commands sharing a topic would
    // make the answer ambiguous.
    [Theory]
    [MemberData(nameof(Commands))]
    public void CommandFor_TheCommandsOwnTopic_NamesTheCommandAgain(string command)
    {
        var topic = VehicleCommands.TopicFor(command);

        Assert.NotNull(topic);
        Assert.Equal(command, VehicleCommands.CommandFor(topic));
    }

    // The gateway registers its schedule handler on drivetrain/chargingSchedule/set; anything
    // else is answered with "Failed: No handler found for command topic ...".
    [Fact]
    public void TopicFor_ScheduledCharging_IsTheTopicTheGatewayHandles()
    {
        Assert.Equal("drivetrain/chargingSchedule", VehicleCommands.TopicFor("scheduled-charging"));
    }

    // The topics saic-python-mqtt-gateway 0.13.0-rc3 registers its handlers on (src/mqtt_topics.py).
    [Theory]
    [InlineData("charge-target", "drivetrain/socTarget")]
    [InlineData("charging", "drivetrain/charging")]
    [InlineData("charging-cable-lock", "drivetrain/chargingCableLock")]
    [InlineData("battery-heating", "drivetrain/batteryHeating")]
    [InlineData("boot", "doors/boot")]
    [InlineData("battery-heating-schedule", "drivetrain/batteryHeatingSchedule")]
    public void TopicFor_PlugInAndBootCommands_AreTheTopicsTheGatewayHandles(string command, string topic)
    {
        Assert.Equal(topic, VehicleCommands.TopicFor(command));
    }

    [Fact]
    public void TopicFor_UnknownCommand_IsNull()
    {
        Assert.Null(VehicleCommands.TopicFor("self-destruct"));
    }

    [Fact]
    public void CommandFor_TopicTheApiNeverSends_IsNull()
    {
        Assert.Null(VehicleCommands.CommandFor("drivetrain/hvBatteryActive"));
    }

    private static Vehicle Car(string? batteryType) => new()
    {
        Vin = "FAKEVN00000000001",
        ConfigJson = batteryType is null ? null : JsonSerializer.Serialize(new Dictionary<string, string> { ["BType"] = batteryType }),
    };

    // The gateway drops a charge target on any other car while answering "Success".
    [Theory]
    [InlineData("1", true)]
    [InlineData("2", false)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void Supports_ChargeTarget_OnlyWhereTheGatewayCarriesItOut(string? batteryType, bool expected)
    {
        Assert.Equal(expected, VehicleCommands.Supports("charge-target", Car(batteryType), "80"));
    }

    // The car answers these for itself, so whatever drivetrain GarageStack detected, they go out.
    [Theory]
    [InlineData("charging")]
    [InlineData("charging-cable-lock")]
    [InlineData("battery-heating")]
    [InlineData("boot")]
    [InlineData("lock")]
    public void Supports_CommandsTheCarAnswersItself_OnEveryCar(string command)
    {
        Assert.True(VehicleCommands.Supports(command, Car("0"), "true"));
    }

    // Charging until the target is dropped, like the target itself, on a car that takes none;
    // charging until a time works on every plug-in car.
    [Theory]
    [InlineData("UNTIL_CONFIGURED_SOC", "1", true)]
    [InlineData("UNTIL_CONFIGURED_SOC", "2", false)]
    [InlineData("UNTIL_CONFIGURED_TIME", "2", true)]
    [InlineData("DISABLED", "2", true)]
    public void Supports_ChargingSchedule_UntilTheTargetOnlyWhereThereIsOne(string mode, string batteryType, bool expected)
    {
        var value = $$"""{"mode":"{{mode}}","startTime":"23:00","endTime":"07:00"}""";

        Assert.Equal(expected, VehicleCommands.Supports("scheduled-charging", Car(batteryType), value));
    }
}

public class CommandResultMessageTests
{
    [Fact]
    public void From_AnswerToAnApiCommand_IsNamedByThatCommand()
    {
        var payload = new CommandResultPayload(1, "FAKEVN00000000001", "doors/locked", false, "vehicle is not online");

        Assert.Equal(new CommandResultMessage("lock", false, "vehicle is not online"), CommandResultMessage.From(payload));
    }

    // Home Assistant sends commands through the same broker; no browser is waiting on those.
    [Fact]
    public void From_AnswerToACommandTheApiNeverSends_IsNull()
    {
        var payload = new CommandResultPayload(1, "FAKEVN00000000001", "drivetrain/hvBatteryActive", true, null);

        Assert.Null(CommandResultMessage.From(payload));
    }
}

/// <summary>
/// The Worker serializes this onto the command_result channel and the Api deserializes it again,
/// so the round-trip is what matters here.
/// </summary>
public class CommandResultPayloadTests
{
    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var payload = new CommandResultPayload(3, "FAKEVN00000000001", "climate/remoteClimateState", false, "vehicle is not online");

        Assert.Equal(payload, CommandResultPayload.FromJson(payload.ToJson()));
    }

    [Fact]
    public void Json_UsesCamelCaseNames()
    {
        var payload = new CommandResultPayload(3, "FAKEVN00000000001", "doors/locked", true, null);

        using var doc = JsonDocument.Parse(payload.ToJson());

        Assert.Equal(3, doc.RootElement.GetProperty("vehicleId").GetInt32());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }
}
