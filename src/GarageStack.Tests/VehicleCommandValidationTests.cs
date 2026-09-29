using GarageStack.Api;

namespace GarageStack.Tests;

public class VehicleCommandValidationTests
{
    // The code every refused value is answered with, which the frontend translates.
    private const string Refused = "command.invalidValue";

    // ── climate / rear-defroster ─────────────────────────────────────────────
    [Theory]
    [InlineData("climate", "on")]
    [InlineData("climate", "off")]
    [InlineData("rear-defroster", "on")]
    [InlineData("rear-defroster", "off")]
    public void OnOffCommands_ValidValues_ReturnsNull(string command, string value)
    {
        Assert.Null(VehicleCommands.Validate(command, value));
    }

    [Theory]
    [InlineData("climate", "start")]
    [InlineData("climate", "ON")]
    [InlineData("rear-defroster", "true")]
    public void OnOffCommands_InvalidValues_ReturnsError(string command, string value)
    {
        Assert.Equal(Refused, VehicleCommands.Validate(command, value)?.Code);
    }

    // ── climate-temperature ───────────────────────────────────────────────────
    [Theory]
    [InlineData("16")]
    [InlineData("22")]
    [InlineData("28")]
    public void ClimateTemperature_ValidRange_ReturnsNull(string value)
    {
        Assert.Null(VehicleCommands.Validate("climate-temperature", value));
    }

    [Theory]
    [InlineData("15")]
    [InlineData("29")]
    [InlineData("abc")]
    [InlineData("22.5")]
    public void ClimateTemperature_InvalidValues_ReturnsError(string value)
    {
        Assert.Equal(Refused, VehicleCommands.Validate("climate-temperature", value)?.Code);
    }

    // ── seat-left / seat-right ───────────────────────────────────────────────
    [Theory]
    [InlineData("seat-left", "0")]
    [InlineData("seat-left", "3")]
    [InlineData("seat-right", "1")]
    [InlineData("seat-right", "2")]
    public void SeatCommands_ValidRange_ReturnsNull(string command, string value)
    {
        Assert.Null(VehicleCommands.Validate(command, value));
    }

    [Theory]
    [InlineData("seat-left", "-1")]
    [InlineData("seat-left", "4")]
    [InlineData("seat-right", "high")]
    public void SeatCommands_InvalidValues_ReturnsError(string command, string value)
    {
        Assert.Equal(Refused, VehicleCommands.Validate(command, value)?.Code);
    }

    // ── find-my-car ───────────────────────────────────────────────────────────
    [Theory]
    [InlineData("activate")]
    [InlineData("stop")]
    public void FindMyCar_ValidValues_ReturnsNull(string value)
    {
        Assert.Null(VehicleCommands.Validate("find-my-car", value));
    }

    [Theory]
    [InlineData("start")]
    [InlineData("Activate")]
    [InlineData("on")]
    public void FindMyCar_InvalidValues_ReturnsError(string value)
    {
        Assert.Equal(Refused, VehicleCommands.Validate("find-my-car", value)?.Code);
    }

    // ── charge-limit ─────────────────────────────────────────────────────────
    // saic-mqtt-gateway maps drivetrain/chargeCurrentLimit/set onto ChargeCurrentLimitCode,
    // whose only members are 6A, 8A, 16A and MAX. It upper-cases the payload before the lookup,
    // so casing does not matter. Earlier this endpoint expected a percentage, which meant every
    // charge-limit button in the UI was answered with 400.
    [Theory]
    [InlineData("6A")]
    [InlineData("8A")]
    [InlineData("16A")]
    [InlineData("MAX")]
    [InlineData("max")]
    [InlineData("Max")]
    public void ChargeLimit_GatewayValues_ReturnsNull(string value)
    {
        Assert.Null(VehicleCommands.Validate("charge-limit", value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("80")]
    [InlineData("100")]
    [InlineData("12A")]
    [InlineData("6")]
    [InlineData("")]
    public void ChargeLimit_InvalidValues_ReturnsError(string value)
    {
        Assert.Equal(Refused, VehicleCommands.Validate("charge-limit", value)?.Code);
    }

    // ── lock ─────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("True")]
    [InlineData("False")]
    public void Lock_ValidValues_ReturnsNull(string value)
    {
        Assert.Null(VehicleCommands.Validate("lock", value));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("lock")]
    [InlineData("1")]
    public void Lock_InvalidValues_ReturnsError(string value)
    {
        Assert.Equal(Refused, VehicleCommands.Validate("lock", value)?.Code);
    }

    // ── refresh ───────────────────────────────────────────────────────────────
    [Fact]
    public void Refresh_ForceValue_ReturnsNull()
    {
        Assert.Null(VehicleCommands.Validate("refresh", "force"));
    }

    [Theory]
    [InlineData("Force")]
    [InlineData("full")]
    public void Refresh_InvalidValues_ReturnsError(string value)
    {
        Assert.Equal(Refused, VehicleCommands.Validate("refresh", value)?.Code);
    }

    // ── scheduled-charging (passthrough) ─────────────────────────────────────
    [Theory]
    [InlineData("scheduled-charging", "any-complex-value")]
    [InlineData("scheduled-charging", "{}")]
    public void ScheduledCharging_AnyNonEmptyString_ReturnsNull(string command, string value)
    {
        Assert.Null(VehicleCommands.Validate(command, value));
    }

    [Fact]
    public void ScheduledCharging_ValueOver500Chars_ReturnsError()
    {
        var value = new string('a', 501);
        Assert.Equal(Refused, VehicleCommands.Validate("scheduled-charging", value)?.Code);
    }

    [Fact]
    public void ScheduledCharging_ValueExactly500Chars_ReturnsNull()
    {
        var value = new string('a', 500);
        Assert.Null(VehicleCommands.Validate("scheduled-charging", value));
    }
}
