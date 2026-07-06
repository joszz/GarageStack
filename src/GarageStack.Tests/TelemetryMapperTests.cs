using GarageStack.Core.Models;
using GarageStack.Worker.Mqtt;

namespace GarageStack.Tests;

public class TelemetryMapperTests
{
    [Fact]
    public void ApplyMessage_FuelLevel_SetsPercent()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/fuelLevel", "72.5");
        Assert.Equal(72.5, snapshot.FuelLevelPercent);
    }

    [Fact]
    public void ApplyMessage_Locked_SetsIsLocked()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "doors/locked", "true");
        Assert.True(snapshot.IsLocked);
    }

    [Fact]
    public void ApplyMessage_Unlocked_SetsIsLockedFalse()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "doors/locked", "false");
        Assert.False(snapshot.IsLocked);
    }

    [Fact]
    public void ApplyMessage_Latitude_SetsCoordinate()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "location/latitude", "52.3676");
        Assert.Equal(52.3676, snapshot.Latitude);
    }

    [Fact]
    public void ApplyMessage_UnknownSubtopic_LeavesSnapshotUnchanged()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "unknown/topic", "somevalue");
        Assert.Null(snapshot.FuelLevelPercent);
        Assert.Null(snapshot.IsLocked);
    }

    // ── Return value ─────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_KnownTopic_ReturnsTrue()
    {
        var snapshot = new TelemetrySnapshot();
        var result = TelemetryMapper.ApplyMessage(snapshot, "drivetrain/fuelLevel", "50");
        Assert.True(result);
    }

    [Fact]
    public void ApplyMessage_UnknownTopic_ReturnsFalse()
    {
        var snapshot = new TelemetrySnapshot();
        var result = TelemetryMapper.ApplyMessage(snapshot, "not/a/real/topic", "42");
        Assert.False(result);
    }

    // ── Fuel topic aliases ────────────────────────────────────────────────────

    [Theory]
    [InlineData("drivetrain/fossilFuel/percentage")]
    [InlineData("drivetrain/fuelLevelPercent")]
    [InlineData("drivetrain/fuelLevel")]
    public void ApplyMessage_FuelLevelAliases_SetFuelLevelPercent(string subtopic)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, subtopic, "80.0");
        Assert.Equal(80.0, snapshot.FuelLevelPercent);
    }

    [Theory]
    [InlineData("drivetrain/fossilFuel/range")]
    [InlineData("drivetrain/fuelRange")]
    public void ApplyMessage_FuelRangeAliases_SetFuelRangeKm(string subtopic)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, subtopic, "300.5");
        Assert.Equal(300.5, snapshot.FuelRangeKm);
    }

    [Theory]
    [InlineData("drivetrain/mileage")]
    [InlineData("drivetrain/odometer")]
    public void ApplyMessage_OdometerAliases_SetOdometerKm(string subtopic)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, subtopic, "12345.6");
        Assert.Equal(12345.6, snapshot.OdometerKm);
    }

    // ── Bool payload variants ─────────────────────────────────────────────────

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("on")]
    public void ApplyMessage_TruthyPayloads_SetEngineRunningTrue(string payload)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/running", payload);
        Assert.True(snapshot.EngineRunning);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("0")]
    [InlineData("off")]
    public void ApplyMessage_FalsyPayloads_SetEngineRunningFalse(string payload)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/running", payload);
        Assert.False(snapshot.EngineRunning);
    }

    // ── Non-finite / invalid numeric ──────────────────────────────────────────

    [Fact]
    public void ApplyMessage_NonNumericPayloadOnNumericField_SetsNull()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/fuelLevel", "notanumber");
        Assert.Null(snapshot.FuelLevelPercent);
    }

    [Fact]
    public void ApplyMessage_EmptyPayloadOnNumericField_SetsNull()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "location/latitude", "");
        Assert.Null(snapshot.Latitude);
    }

    // ── Speed ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("drivetrain/speed")]
    [InlineData("location/speed")]
    public void ApplyMessage_SpeedAliases_SetSpeed(string subtopic)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, subtopic, "87.0");
        Assert.Equal(87.0, snapshot.Speed);
    }

    // ── Battery voltage aliases ───────────────────────────────────────────────

    [Theory]
    [InlineData("drivetrain/auxiliaryBatteryVoltage")]
    [InlineData("12v/batteryVoltage")]
    [InlineData("battery/voltage")]
    public void ApplyMessage_BatteryVoltageAliases_SetBatteryVoltage(string subtopic)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, subtopic, "12.4");
        Assert.Equal(12.4, snapshot.BatteryVoltage);
    }

    // ── EV / PHEV ─────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_Soc_SetsEvSocPercent()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/soc", "65.0");
        Assert.Equal(65.0, snapshot.EvSocPercent);
    }

    [Fact]
    public void ApplyMessage_Charging_SetsIsCharging()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/charging", "true");
        Assert.True(snapshot.IsCharging);
    }

    // ── Doors ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_DriverDoor_SetsDriverDoorOpen()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "doors/driver", "open");
        Assert.True(snapshot.DriverDoorOpen);
    }

    [Fact]
    public void ApplyMessage_PassengerDoor_SetsPassengerDoorOpen()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "doors/passenger", "open");
        Assert.True(snapshot.PassengerDoorOpen);
    }

    [Fact]
    public void ApplyMessage_RearLeftDoor_SetsRearLeftDoorOpen()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "doors/rearLeft", "open");
        Assert.True(snapshot.RearLeftDoorOpen);
    }

    [Fact]
    public void ApplyMessage_RearRightDoor_SetsRearRightDoorOpen()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "doors/rearRight", "open");
        Assert.True(snapshot.RearRightDoorOpen);
    }

    [Theory]
    [InlineData("doors/boot")]
    [InlineData("doors/trunk")]
    public void ApplyMessage_TrunkAliases_SetTrunkOpen(string subtopic)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, subtopic, "open");
        Assert.True(snapshot.TrunkOpen);
    }

    [Theory]
    [InlineData("doors/bonnet")]
    [InlineData("doors/hood")]
    public void ApplyMessage_BonnetAliases_SetBonnetOpen(string subtopic)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, subtopic, "open");
        Assert.True(snapshot.BonnetOpen);
    }

    // ── Windows ───────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_DriverWindow_SetsDriverWindowOpen()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "windows/driver", "open");
        Assert.True(snapshot.DriverWindowOpen);
    }

    [Fact]
    public void ApplyMessage_PassengerWindow_SetsPassengerWindowOpen()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "windows/passenger", "open");
        Assert.True(snapshot.PassengerWindowOpen);
    }

    [Fact]
    public void ApplyMessage_RearLeftWindow_SetsRearLeftWindowOpen()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "windows/rearLeft", "open");
        Assert.True(snapshot.RearLeftWindowOpen);
    }

    [Fact]
    public void ApplyMessage_RearRightWindow_SetsRearRightWindowOpen()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "windows/rearRight", "open");
        Assert.True(snapshot.RearRightWindowOpen);
    }

    [Fact]
    public void ApplyMessage_SunRoof_SetsSunRoofOpen()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "windows/sunRoof", "open");
        Assert.True(snapshot.SunRoofOpen);
    }

    // ── Location ──────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_Longitude_SetsLongitude()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "location/longitude", "4.9041");
        Assert.Equal(4.9041, snapshot.Longitude);
    }

    [Fact]
    public void ApplyMessage_Heading_SetsHeading()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "location/heading", "270.0");
        Assert.Equal(270.0, snapshot.Heading);
    }

    [Fact]
    public void ApplyMessage_LocationPosition_ParsesLatLon()
    {
        var snapshot = new TelemetrySnapshot();
        var json = """{"latitude":51.5074,"longitude":-0.1278}""";
        TelemetryMapper.ApplyMessage(snapshot, "location/position", json);
        Assert.Equal(51.5074, snapshot.Latitude);
        Assert.Equal(-0.1278, snapshot.Longitude);
    }

    [Fact]
    public void ApplyMessage_LocationPosition_MalformedJson_ReturnsFalse()
    {
        var snapshot = new TelemetrySnapshot();
        var result = TelemetryMapper.ApplyMessage(snapshot, "location/position", "{not valid json");
        Assert.False(result);
    }

    [Fact]
    public void ApplyMessage_Elevation_SetsElevation()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "location/elevation", "42.5");
        Assert.Equal(42.5, snapshot.Elevation);
    }

    // ── Climate ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("climate/remoteClimateState")]
    [InlineData("climate/on")]
    [InlineData("climate/active")]
    public void ApplyMessage_ClimateAliases_SetClimateOn(string subtopic)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, subtopic, "true");
        Assert.True(snapshot.ClimateOn);
    }

    [Fact]
    public void ApplyMessage_InteriorTemperature_SetsInteriorTemperature()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "climate/interiorTemperature", "21.5");
        Assert.Equal(21.5, snapshot.InteriorTemperature);
    }

    [Fact]
    public void ApplyMessage_RemoteTemperature_SetsRemoteTemperature()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "climate/remoteTemperature", "18.0");
        Assert.Equal(18.0, snapshot.RemoteTemperature);
    }

    [Fact]
    public void ApplyMessage_ExteriorTemperature_SetsExteriorTemperature()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "climate/exteriorTemperature", "10.0");
        Assert.Equal(10.0, snapshot.ExteriorTemperature);
    }

    [Fact]
    public void ApplyMessage_RearWindowDefroster_SetsRearWindowDefroster()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "climate/rearWindowDefrosterHeating", "true");
        Assert.True(snapshot.RearWindowDefroster);
    }

    [Fact]
    public void ApplyMessage_HeatedSeatFrontLeft_SetsLevel()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "climate/heatedSeatsFrontLeftLevel", "2");
        Assert.Equal(2, snapshot.HeatedSeatFrontLeft);
    }

    [Fact]
    public void ApplyMessage_HeatedSeatFrontRight_SetsLevel()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "climate/heatedSeatsFrontRightLevel", "3");
        Assert.Equal(3, snapshot.HeatedSeatFrontRight);
    }

    // ── Tyres ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_TyrePressureFrontLeft_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "tyres/frontLeftPressure", "2.4");
        Assert.Equal(2.4, snapshot.TyrePressureFrontLeft);
    }

    [Fact]
    public void ApplyMessage_TyrePressureFrontRight_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "tyres/frontRightPressure", "2.4");
        Assert.Equal(2.4, snapshot.TyrePressureFrontRight);
    }

    [Fact]
    public void ApplyMessage_TyrePressureRearLeft_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "tyres/rearLeftPressure", "2.2");
        Assert.Equal(2.2, snapshot.TyrePressureRearLeft);
    }

    [Fact]
    public void ApplyMessage_TyrePressureRearRight_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "tyres/rearRightPressure", "2.2");
        Assert.Equal(2.2, snapshot.TyrePressureRearRight);
    }

    // ── HV drivetrain ─────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_HvVoltage_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/voltage", "380.0");
        Assert.Equal(380.0, snapshot.HvVoltage);
    }

    [Fact]
    public void ApplyMessage_HvCurrent_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/current", "15.5");
        Assert.Equal(15.5, snapshot.HvCurrent);
    }

    [Fact]
    public void ApplyMessage_HvPower_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/power", "5890.0");
        Assert.Equal(5890.0, snapshot.HvPower);
    }

    [Fact]
    public void ApplyMessage_HvSocKwh_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/soc_kwh", "42.0");
        Assert.Equal(42.0, snapshot.HvSocKwh);
    }

    [Fact]
    public void ApplyMessage_TotalBatteryCapacity_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/totalBatteryCapacity", "72.6");
        Assert.Equal(72.6, snapshot.HvTotalCapacityKwh);
    }

    [Fact]
    public void ApplyMessage_ChargerConnected_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/chargerConnected", "true");
        Assert.True(snapshot.ChargerConnected);
    }

    [Fact]
    public void ApplyMessage_HvBatteryActive_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/hvBatteryActive", "true");
        Assert.True(snapshot.HvBatteryActive);
    }

    // ── Lights ────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_MainBeam_SetsLightsMainBeam()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "lights/mainBeam", "true");
        Assert.True(snapshot.LightsMainBeam);
    }

    [Fact]
    public void ApplyMessage_DippedBeam_SetsLightsDippedBeam()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "lights/dippedBeam", "true");
        Assert.True(snapshot.LightsDippedBeam);
    }

    [Fact]
    public void ApplyMessage_SideLights_SetsLightsSide()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "lights/side", "true");
        Assert.True(snapshot.LightsSide);
    }

    // ── Online / availability ─────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_Available_SetsIsAvailable()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "available", "online");
        Assert.True(snapshot.IsAvailable);
    }

    [Fact]
    public void ApplyMessage_Unavailable_SetsIsAvailableFalse()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "available", "offline");
        Assert.False(snapshot.IsAvailable);
    }

    // ── Refresh timestamps ────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_LastVehicleState_ParsesDateTime()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "refresh/lastVehicleState", "2024-06-01T12:00:00Z");
        Assert.Equal(new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc), snapshot.LastVehicleStateAt);
    }

    [Fact]
    public void ApplyMessage_LastChargeState_ParsesDateTime()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "refresh/lastChargeState", "2024-05-15T08:30:00Z");
        Assert.Equal(new DateTime(2024, 5, 15, 8, 30, 0, DateTimeKind.Utc), snapshot.LastChargeStateAt);
    }

    // ── Active journey ────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_CurrentJourneyDistance_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/currentJourney/distance", "12.3");
        Assert.Equal(12.3, snapshot.CurrentJourneyDistance);
    }

    [Fact]
    public void ApplyMessage_CurrentJourneyJson_ExtractsDistance()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/currentJourney", """{"distance":8.5,"other":"data"}""");
        Assert.Equal(8.5, snapshot.CurrentJourneyDistance);
    }

    [Fact]
    public void ApplyMessage_CurrentJourneyJson_MalformedJson_ReturnsFalse()
    {
        var snapshot = new TelemetrySnapshot();
        var result = TelemetryMapper.ApplyMessage(snapshot, "drivetrain/currentJourney", "{bad json");
        Assert.False(result);
    }

    // ── Charging session ──────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_ChargingType_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/chargingType", "AC");
        Assert.Equal("AC", snapshot.ChargingType);
    }

    [Fact]
    public void ApplyMessage_ChargingCableLock_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/chargingCableLock", "true");
        Assert.True(snapshot.ChargingCableLock);
    }

    [Fact]
    public void ApplyMessage_RemainingChargingTime_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/remainingChargingTime", "45");
        Assert.Equal(45, snapshot.RemainingChargingTime);
    }

    [Fact]
    public void ApplyMessage_LastChargeEndingPower_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/lastChargeEndingPower", "7.2");
        Assert.Equal(7.2, snapshot.LastChargeEndingPower);
    }

    [Fact]
    public void ApplyMessage_ChargingLastEnd_ConvertsFromUnixEpoch()
    {
        var snapshot = new TelemetrySnapshot();
        // 2024-01-01 00:00:00 UTC = 1704067200 seconds
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/charging/lastEnd", "1704067200");
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), snapshot.ChargingLastEndAt);
    }

    [Fact]
    public void ApplyMessage_ChargingScheduleJson_ExtractsFields()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/chargingSchedule",
            """{"mode":"timed","startTime":"22:00","endTime":"06:00"}""");
        Assert.Equal("timed", snapshot.ChargingScheduleMode);
        Assert.Equal("22:00", snapshot.ChargingScheduleStartTime);
        Assert.Equal("06:00", snapshot.ChargingScheduleEndTime);
    }

    [Fact]
    public void ApplyMessage_ChargingScheduleJson_MalformedJson_ReturnsFalse()
    {
        var snapshot = new TelemetrySnapshot();
        var result = TelemetryMapper.ApplyMessage(snapshot, "drivetrain/chargingSchedule", "{bad}");
        Assert.False(result);
    }

    [Fact]
    public void ApplyMessage_BmsChargeStatus_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "bms/chargeStatus", "charging");
        Assert.Equal("charging", snapshot.BmsChargeStatus);
    }

    [Fact]
    public void ApplyMessage_OnboardChargerPlugStatus_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "ccu/onboardChargerPlugStatus", "1");
        Assert.Equal(1, snapshot.OnboardChargerPlugStatus);
    }

    [Fact]
    public void ApplyMessage_OffboardChargerPlugStatus_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "ccu/offboardChargerPlugStatus", "2");
        Assert.Equal(2, snapshot.OffboardChargerPlugStatus);
    }

    // ── OBC ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_ObcCurrent_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "obc/current", "16.0");
        Assert.Equal(16.0, snapshot.ObcCurrent);
    }

    [Fact]
    public void ApplyMessage_ObcVoltage_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "obc/voltage", "230.0");
        Assert.Equal(230.0, snapshot.ObcVoltage);
    }

    [Fact]
    public void ApplyMessage_ObcPowerSinglePhase_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "obc/powerSinglePhase", "3680.0");
        Assert.Equal(3680.0, snapshot.ObcPowerSinglePhase);
    }

    [Fact]
    public void ApplyMessage_ObcPowerThreePhase_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "obc/powerThreePhase", "11000.0");
        Assert.Equal(11000.0, snapshot.ObcPowerThreePhase);
    }

    // ── Battery heating ───────────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_BatteryHeating_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/batteryHeating", "true");
        Assert.True(snapshot.BatteryHeating);
    }

    [Fact]
    public void ApplyMessage_BatteryHeatingScheduleMode_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/batteryHeatingSchedule/mode", "timed");
        Assert.Equal("timed", snapshot.BatteryHeatingScheduleMode);
    }

    [Fact]
    public void ApplyMessage_BatteryHeatingScheduleStartTime_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/batteryHeatingSchedule/startTime", "07:00");
        Assert.Equal("07:00", snapshot.BatteryHeatingScheduleStartTime);
    }

    [Fact]
    public void ApplyMessage_BatteryHeatingScheduleJson_ExtractsFields()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/batteryHeatingSchedule",
            """{"mode":"timed","startTime":"07:30"}""");
        Assert.Equal("timed", snapshot.BatteryHeatingScheduleMode);
        Assert.Equal("07:30", snapshot.BatteryHeatingScheduleStartTime);
    }

    [Fact]
    public void ApplyMessage_BatteryHeatingScheduleJson_MalformedJson_ReturnsFalse()
    {
        var snapshot = new TelemetrySnapshot();
        var result = TelemetryMapper.ApplyMessage(snapshot, "drivetrain/batteryHeatingSchedule", "{bad json");
        Assert.False(result);
    }

    // ── Daily efficiency stats ────────────────────────────────────────────────

    [Fact]
    public void ApplyMessage_MileageOfTheDay_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/mileageOfTheDay", "45.2");
        Assert.Equal(45.2, snapshot.MileageOfTheDay);
    }

    [Fact]
    public void ApplyMessage_PowerUsageOfDay_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/powerUsageOfDay", "8.5");
        Assert.Equal(8.5, snapshot.PowerUsageOfDay);
    }

    [Fact]
    public void ApplyMessage_MileageSinceLastCharge_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/mileageSinceLastCharge", "120.0");
        Assert.Equal(120.0, snapshot.MileageSinceLastCharge);
    }

    [Fact]
    public void ApplyMessage_PowerUsageSinceLastCharge_SetsValue()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/powerUsageSinceLastCharge", "22.3");
        Assert.Equal(22.3, snapshot.PowerUsageSinceLastCharge);
    }
}
