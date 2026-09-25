using GarageStack.Worker.Mqtt;

namespace GarageStack.Tests;

// Payloads as saic-python-mqtt-gateway 0.12.0 publishes them (handlers/vehicle_command.py).
public class GatewayCommandResultTests
{
    [Fact]
    public void TryParse_Success_NamesTheCommandTopic()
    {
        Assert.True(GatewayCommandResult.TryParse("doors/locked/result", "Success", out var result));

        Assert.Equal(new GatewayCommandResult("doors/locked", true, null), result);
    }

    [Fact]
    public void TryParse_Failure_CarriesTheReason()
    {
        Assert.True(GatewayCommandResult.TryParse(
            "climate/remoteClimateState/result", "Failed: vehicle is not online", out var result));

        Assert.Equal(new GatewayCommandResult("climate/remoteClimateState", false, "vehicle is not online"), result);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Failed: ")]
    public void TryParse_FailureWithoutReason_HasNoDetail(string payload)
    {
        Assert.True(GatewayCommandResult.TryParse("doors/locked/result", payload, out var result));

        Assert.False(result.Success);
        Assert.Null(result.Detail);
    }

    // The reason is logged and shown on screen, so a multi-line exception text becomes one line.
    [Fact]
    public void TryParse_ReasonOnSeveralLines_IsJoinedOntoOne()
    {
        Assert.True(GatewayCommandResult.TryParse("doors/locked/result", "Failed: first\nsecond", out var result));

        Assert.Equal("first second", result.Detail);
    }

    [Fact]
    public void TryParse_OverlongReason_IsCut()
    {
        var reason = new string('x', GatewayCommandResult.MaxDetailLength + 50);

        Assert.True(GatewayCommandResult.TryParse("doors/locked/result", $"Failed: {reason}", out var result));

        Assert.Equal(GatewayCommandResult.MaxDetailLength, result.Detail!.Length);
    }

    [Theory]
    [InlineData("doors/locked", "Success")] // the state topic, not its result
    [InlineData("doors/locked/set", "True")] // the command itself, echoed back by the broker
    [InlineData("result", "Success")] // no command in front of the suffix
    [InlineData("doors/locked/result", "")] // a cleared retained result
    [InlineData("doors/locked/result", "Done")] // not an answer this gateway gives
    public void TryParse_NotAGatewayAnswer_ReturnsFalse(string subtopic, string payload)
    {
        Assert.False(GatewayCommandResult.TryParse(subtopic, payload, out _));
    }
}
