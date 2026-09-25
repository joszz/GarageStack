using System.Text.Json;
using GarageStack.Api;
using GarageStack.Api.Hubs;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class VehicleCommandsTests
{
    public static TheoryData<string> Commands() =>
    [
        "climate", "climate-temperature", "rear-defroster", "seat-left", "seat-right",
        "find-my-car", "charge-limit", "scheduled-charging", "lock", "refresh",
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

    [Fact]
    public void TopicFor_UnknownCommand_IsNull()
    {
        Assert.Null(VehicleCommands.TopicFor("self-destruct"));
    }

    [Fact]
    public void CommandFor_TopicTheApiNeverSends_IsNull()
    {
        Assert.Null(VehicleCommands.CommandFor("drivetrain/socTarget"));
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
        var payload = new CommandResultPayload(1, "FAKEVN00000000001", "drivetrain/socTarget", true, null);

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
