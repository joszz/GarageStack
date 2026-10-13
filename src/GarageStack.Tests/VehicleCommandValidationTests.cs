using GarageStack.Core.Helpers;

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
        Assert.Null(VehicleCommands.Validate(command, value));
    }

    // The gateway's two other remote climate modes: front window defrost and fan only.
    [Theory]
    [InlineData("front")]
    [InlineData("blowingonly")]
    public void Climate_GatewayModes_ReturnsNull(string value)
    {
        Assert.Null(VehicleCommands.Validate("climate", value));
    }

    [Theory]
    [InlineData("climate", "start")]
    [InlineData("climate", "ON")]
    [InlineData("climate", "Front")]
    [InlineData("rear-defroster", "true")]
    [InlineData("rear-defroster", "front")]
    public void OnOffCommands_InvalidValues_ReturnsError(string command, string value)
    {
        Assert.NotNull(VehicleCommands.Validate(command, value));
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
        Assert.NotNull(VehicleCommands.Validate("climate-temperature", value));
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
        Assert.NotNull(VehicleCommands.Validate(command, value));
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
        Assert.NotNull(VehicleCommands.Validate("find-my-car", value));
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
        Assert.NotNull(VehicleCommands.Validate("charge-limit", value));
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
        Assert.NotNull(VehicleCommands.Validate("lock", value));
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
        Assert.NotNull(VehicleCommands.Validate("refresh", value));
    }

    // ── charge-target ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData("40")]
    [InlineData("80")]
    [InlineData("100")]
    public void ChargeTarget_StepsTheGatewayKnows_ReturnsNull(string value)
    {
        Assert.Null(VehicleCommands.Validate("charge-target", value));
    }

    [Theory]
    [InlineData("30")]
    [InlineData("85")]
    [InlineData("110")]
    [InlineData("80%")]
    public void ChargeTarget_OtherValues_ReturnsError(string value)
    {
        Assert.NotNull(VehicleCommands.Validate("charge-target", value));
    }

    // ── charging / charging-cable-lock / battery-heating ──────────────────────
    [Theory]
    [InlineData("charging", "true")]
    [InlineData("charging", "false")]
    [InlineData("charging-cable-lock", "true")]
    [InlineData("charging-cable-lock", "false")]
    [InlineData("battery-heating", "true")]
    [InlineData("battery-heating", "false")]
    public void TrueFalseCommands_ValidValues_ReturnsNull(string command, string value)
    {
        Assert.Null(VehicleCommands.Validate(command, value));
    }

    [Theory]
    [InlineData("charging", "on")]
    [InlineData("charging", "True")]
    [InlineData("charging-cable-lock", "1")]
    [InlineData("battery-heating", "start")]
    public void TrueFalseCommands_InvalidValues_ReturnsError(string command, string value)
    {
        Assert.NotNull(VehicleCommands.Validate(command, value));
    }

    // ── boot ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Boot_Open_ReturnsNull()
    {
        Assert.Null(VehicleCommands.Validate("boot", "false"));
    }

    // The gateway answers "true" with "Success" without doing anything: a boot cannot be closed.
    [Theory]
    [InlineData("true")]
    [InlineData("open")]
    public void Boot_AnythingButOpen_ReturnsError(string value)
    {
        Assert.NotNull(VehicleCommands.Validate("boot", value));
    }

    // ── scheduled-charging ────────────────────────────────────────────────────
    // As the gateway reads it: the mode by ScheduledChargingMode's name, both times by
    // time.fromisoformat, and both times even to switch the schedule off.
    [Theory]
    [InlineData("""{"mode":"UNTIL_CONFIGURED_TIME","startTime":"23:00","endTime":"07:00"}""")]
    [InlineData("""{"mode":"UNTIL_CONFIGURED_SOC","startTime":"01:30","endTime":"06:00"}""")]
    [InlineData("""{"mode":"DISABLED","startTime":"00:00","endTime":"00:00"}""")]
    public void ScheduledCharging_WhatTheGatewayReads_ReturnsNull(string value)
    {
        Assert.Null(VehicleCommands.Validate("scheduled-charging", value));
    }

    [Theory]
    [InlineData("any-complex-value")]
    [InlineData("{}")]
    [InlineData("""{"mode":"DISABLED"}""")]
    [InlineData("""{"mode":"SOMETIMES","startTime":"23:00","endTime":"07:00"}""")]
    [InlineData("""{"mode":"UNTIL_CONFIGURED_TIME","startTime":"24:00","endTime":"07:00"}""")]
    [InlineData("""{"mode":"UNTIL_CONFIGURED_TIME","startTime":"7:00","endTime":"07:00"}""")]
    [InlineData("""{"mode":"UNTIL_CONFIGURED_TIME","startTime":23,"endTime":"07:00"}""")]
    public void ScheduledCharging_AnythingElse_ReturnsError(string value)
    {
        Assert.NotNull(VehicleCommands.Validate("scheduled-charging", value));
    }

    [Fact]
    public void ScheduledCharging_ValueOver500Chars_ReturnsError()
    {
        // A valid schedule padded past the cap, so only the length can refuse it.
        var value = "{\"mode\":\"DISABLED\",\"startTime\":\"00:00\",\"endTime\":\"00:00\",\"x\":\"" + new string('a', 500) + "\"}";
        Assert.NotNull(VehicleCommands.Validate("scheduled-charging", value));
    }

    // ── battery-heating-schedule ─────────────────────────────────────────────
    [Theory]
    [InlineData("""{"mode":"on","startTime":"06:30"}""")]
    [InlineData("""{"mode":"off","startTime":"00:00"}""")]
    public void BatteryHeatingSchedule_WhatTheGatewayReads_ReturnsNull(string value)
    {
        Assert.Null(VehicleCommands.Validate("battery-heating-schedule", value));
    }

    [Theory]
    [InlineData("""{"mode":"off"}""")]
    [InlineData("""{"mode":"daily","startTime":"06:30"}""")]
    [InlineData("""{"mode":"on","startTime":"6.30"}""")]
    [InlineData("on")]
    public void BatteryHeatingSchedule_AnythingElse_ReturnsError(string value)
    {
        Assert.NotNull(VehicleCommands.Validate("battery-heating-schedule", value));
    }
}
