using GarageStack.Api.Endpoints;

namespace GarageStack.Tests;

public class VehicleCommandValidationTests
{
    // ── climate / rear-defroster ─────────────────────────────────────────────
    [Theory]
    [InlineData("climate", "on")]
    [InlineData("climate", "off")]
    [InlineData("rear-defroster", "on")]
    [InlineData("rear-defroster", "off")]
    public void OnOffCommands_ValidValues_ReturnsNull(string command, string value)
    {
        Assert.Null(VehicleEndpoints.ValidateCommandValue(command, value));
    }

    [Theory]
    [InlineData("climate", "start")]
    [InlineData("climate", "ON")]
    [InlineData("rear-defroster", "true")]
    public void OnOffCommands_InvalidValues_ReturnsError(string command, string value)
    {
        Assert.NotNull(VehicleEndpoints.ValidateCommandValue(command, value));
    }

    // ── climate-temperature ───────────────────────────────────────────────────
    [Theory]
    [InlineData("16")]
    [InlineData("22")]
    [InlineData("28")]
    public void ClimateTemperature_ValidRange_ReturnsNull(string value)
    {
        Assert.Null(VehicleEndpoints.ValidateCommandValue("climate-temperature", value));
    }

    [Theory]
    [InlineData("15")]
    [InlineData("29")]
    [InlineData("abc")]
    [InlineData("22.5")]
    public void ClimateTemperature_InvalidValues_ReturnsError(string value)
    {
        Assert.NotNull(VehicleEndpoints.ValidateCommandValue("climate-temperature", value));
    }

    // ── seat-left / seat-right ───────────────────────────────────────────────
    [Theory]
    [InlineData("seat-left", "0")]
    [InlineData("seat-left", "3")]
    [InlineData("seat-right", "1")]
    [InlineData("seat-right", "2")]
    public void SeatCommands_ValidRange_ReturnsNull(string command, string value)
    {
        Assert.Null(VehicleEndpoints.ValidateCommandValue(command, value));
    }

    [Theory]
    [InlineData("seat-left", "-1")]
    [InlineData("seat-left", "4")]
    [InlineData("seat-right", "high")]
    public void SeatCommands_InvalidValues_ReturnsError(string command, string value)
    {
        Assert.NotNull(VehicleEndpoints.ValidateCommandValue(command, value));
    }

    // ── find-my-car ───────────────────────────────────────────────────────────
    [Theory]
    [InlineData("activate")]
    [InlineData("stop")]
    public void FindMyCar_ValidValues_ReturnsNull(string value)
    {
        Assert.Null(VehicleEndpoints.ValidateCommandValue("find-my-car", value));
    }

    [Theory]
    [InlineData("start")]
    [InlineData("Activate")]
    [InlineData("on")]
    public void FindMyCar_InvalidValues_ReturnsError(string value)
    {
        Assert.NotNull(VehicleEndpoints.ValidateCommandValue("find-my-car", value));
    }

    // ── charge-limit ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData("1")]
    [InlineData("80")]
    [InlineData("100")]
    public void ChargeLimit_ValidRange_ReturnsNull(string value)
    {
        Assert.Null(VehicleEndpoints.ValidateCommandValue("charge-limit", value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("max")]
    public void ChargeLimit_InvalidValues_ReturnsError(string value)
    {
        Assert.NotNull(VehicleEndpoints.ValidateCommandValue("charge-limit", value));
    }

    // ── lock ─────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("True")]
    [InlineData("False")]
    public void Lock_ValidValues_ReturnsNull(string value)
    {
        Assert.Null(VehicleEndpoints.ValidateCommandValue("lock", value));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("lock")]
    [InlineData("1")]
    public void Lock_InvalidValues_ReturnsError(string value)
    {
        Assert.NotNull(VehicleEndpoints.ValidateCommandValue("lock", value));
    }

    // ── refresh ───────────────────────────────────────────────────────────────
    [Fact]
    public void Refresh_ForceValue_ReturnsNull()
    {
        Assert.Null(VehicleEndpoints.ValidateCommandValue("refresh", "force"));
    }

    [Theory]
    [InlineData("Force")]
    [InlineData("full")]
    public void Refresh_InvalidValues_ReturnsError(string value)
    {
        Assert.NotNull(VehicleEndpoints.ValidateCommandValue("refresh", value));
    }

    // ── scheduled-charging (passthrough) ─────────────────────────────────────
    [Theory]
    [InlineData("scheduled-charging", "any-complex-value")]
    [InlineData("scheduled-charging", "{}")]
    public void ScheduledCharging_AnyNonEmptyString_ReturnsNull(string command, string value)
    {
        Assert.Null(VehicleEndpoints.ValidateCommandValue(command, value));
    }

    [Fact]
    public void ScheduledCharging_ValueOver500Chars_ReturnsError()
    {
        var value = new string('a', 501);
        Assert.NotNull(VehicleEndpoints.ValidateCommandValue("scheduled-charging", value));
    }

    [Fact]
    public void ScheduledCharging_ValueExactly500Chars_ReturnsNull()
    {
        var value = new string('a', 500);
        Assert.Null(VehicleEndpoints.ValidateCommandValue("scheduled-charging", value));
    }
}
