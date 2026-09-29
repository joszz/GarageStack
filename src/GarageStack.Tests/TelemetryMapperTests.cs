using GarageStack.Core.Models;
using GarageStack.Worker.Mqtt;

namespace GarageStack.Tests;

public class TelemetryMapperTests
{
    private static object? Read(TelemetrySnapshot snapshot, string field) =>
        typeof(TelemetrySnapshot).GetProperty(field)!.GetValue(snapshot);

    // One row per subtopic the mapper knows, aliases included: the mapper's comment explains why
    // a field can have more than one name.
    [Theory]
    // Fuel, odometer and speed
    [InlineData("drivetrain/fossilFuel/percentage", "80.0", nameof(TelemetrySnapshot.FuelLevelPercent), 80.0)]
    [InlineData("drivetrain/fuelLevelPercent", "80.0", nameof(TelemetrySnapshot.FuelLevelPercent), 80.0)]
    [InlineData("drivetrain/fuelLevel", "72.5", nameof(TelemetrySnapshot.FuelLevelPercent), 72.5)]
    [InlineData("drivetrain/fossilFuel/range", "300.5", nameof(TelemetrySnapshot.FuelRangeKm), 300.5)]
    [InlineData("drivetrain/fuelRange", "300.5", nameof(TelemetrySnapshot.FuelRangeKm), 300.5)]
    [InlineData("drivetrain/mileage", "12345.6", nameof(TelemetrySnapshot.OdometerKm), 12345.6)]
    [InlineData("drivetrain/odometer", "12345.6", nameof(TelemetrySnapshot.OdometerKm), 12345.6)]
    [InlineData("drivetrain/speed", "87.0", nameof(TelemetrySnapshot.Speed), 87.0)]
    [InlineData("location/speed", "87.0", nameof(TelemetrySnapshot.Speed), 87.0)]
    // 12 V battery
    [InlineData("drivetrain/auxiliaryBatteryVoltage", "12.4", nameof(TelemetrySnapshot.BatteryVoltage), 12.4)]
    [InlineData("12v/batteryVoltage", "12.4", nameof(TelemetrySnapshot.BatteryVoltage), 12.4)]
    [InlineData("battery/voltage", "12.4", nameof(TelemetrySnapshot.BatteryVoltage), 12.4)]
    // EV / PHEV
    [InlineData("drivetrain/soc", "65.0", nameof(TelemetrySnapshot.EvSocPercent), 65.0)]
    [InlineData("drivetrain/charging", "true", nameof(TelemetrySnapshot.IsCharging), true)]
    // Doors
    [InlineData("doors/locked", "true", nameof(TelemetrySnapshot.IsLocked), true)]
    [InlineData("doors/locked", "false", nameof(TelemetrySnapshot.IsLocked), false)]
    [InlineData("doors/driver", "open", nameof(TelemetrySnapshot.DriverDoorOpen), true)]
    [InlineData("doors/passenger", "open", nameof(TelemetrySnapshot.PassengerDoorOpen), true)]
    [InlineData("doors/rearLeft", "open", nameof(TelemetrySnapshot.RearLeftDoorOpen), true)]
    [InlineData("doors/rearRight", "open", nameof(TelemetrySnapshot.RearRightDoorOpen), true)]
    [InlineData("doors/boot", "open", nameof(TelemetrySnapshot.TrunkOpen), true)]
    [InlineData("doors/trunk", "open", nameof(TelemetrySnapshot.TrunkOpen), true)]
    [InlineData("doors/bonnet", "open", nameof(TelemetrySnapshot.BonnetOpen), true)]
    [InlineData("doors/hood", "open", nameof(TelemetrySnapshot.BonnetOpen), true)]
    // Windows
    [InlineData("windows/driver", "open", nameof(TelemetrySnapshot.DriverWindowOpen), true)]
    [InlineData("windows/passenger", "open", nameof(TelemetrySnapshot.PassengerWindowOpen), true)]
    [InlineData("windows/rearLeft", "open", nameof(TelemetrySnapshot.RearLeftWindowOpen), true)]
    [InlineData("windows/rearRight", "open", nameof(TelemetrySnapshot.RearRightWindowOpen), true)]
    [InlineData("windows/sunRoof", "open", nameof(TelemetrySnapshot.SunRoofOpen), true)]
    // Location
    [InlineData("location/latitude", "52.3676", nameof(TelemetrySnapshot.Latitude), 52.3676)]
    [InlineData("location/longitude", "4.9041", nameof(TelemetrySnapshot.Longitude), 4.9041)]
    [InlineData("location/heading", "270.0", nameof(TelemetrySnapshot.Heading), 270.0)]
    [InlineData("location/elevation", "42.5", nameof(TelemetrySnapshot.Elevation), 42.5)]
    // Climate
    [InlineData("climate/remoteClimateState", "true", nameof(TelemetrySnapshot.ClimateOn), true)]
    [InlineData("climate/on", "true", nameof(TelemetrySnapshot.ClimateOn), true)]
    [InlineData("climate/active", "true", nameof(TelemetrySnapshot.ClimateOn), true)]
    [InlineData("climate/interiorTemperature", "21.5", nameof(TelemetrySnapshot.InteriorTemperature), 21.5)]
    [InlineData("climate/remoteTemperature", "18.0", nameof(TelemetrySnapshot.RemoteTemperature), 18.0)]
    [InlineData("climate/exteriorTemperature", "10.0", nameof(TelemetrySnapshot.ExteriorTemperature), 10.0)]
    [InlineData("climate/rearWindowDefrosterHeating", "true", nameof(TelemetrySnapshot.RearWindowDefroster), true)]
    [InlineData("climate/heatedSeatsFrontLeftLevel", "2", nameof(TelemetrySnapshot.HeatedSeatFrontLeft), 2)]
    [InlineData("climate/heatedSeatsFrontRightLevel", "3", nameof(TelemetrySnapshot.HeatedSeatFrontRight), 3)]
    // Tyres
    [InlineData("tyres/frontLeftPressure", "2.4", nameof(TelemetrySnapshot.TyrePressureFrontLeft), 2.4)]
    [InlineData("tyres/frontRightPressure", "2.4", nameof(TelemetrySnapshot.TyrePressureFrontRight), 2.4)]
    [InlineData("tyres/rearLeftPressure", "2.2", nameof(TelemetrySnapshot.TyrePressureRearLeft), 2.2)]
    [InlineData("tyres/rearRightPressure", "2.2", nameof(TelemetrySnapshot.TyrePressureRearRight), 2.2)]
    // HV drivetrain
    [InlineData("drivetrain/voltage", "380.0", nameof(TelemetrySnapshot.HvVoltage), 380.0)]
    [InlineData("drivetrain/current", "15.5", nameof(TelemetrySnapshot.HvCurrent), 15.5)]
    [InlineData("drivetrain/power", "5890.0", nameof(TelemetrySnapshot.HvPower), 5890.0)]
    [InlineData("drivetrain/soc_kwh", "42.0", nameof(TelemetrySnapshot.HvSocKwh), 42.0)]
    [InlineData("drivetrain/totalBatteryCapacity", "72.6", nameof(TelemetrySnapshot.HvTotalCapacityKwh), 72.6)]
    [InlineData("drivetrain/chargerConnected", "true", nameof(TelemetrySnapshot.ChargerConnected), true)]
    [InlineData("drivetrain/hvBatteryActive", "true", nameof(TelemetrySnapshot.HvBatteryActive), true)]
    // Lights
    [InlineData("lights/mainBeam", "true", nameof(TelemetrySnapshot.LightsMainBeam), true)]
    [InlineData("lights/dippedBeam", "true", nameof(TelemetrySnapshot.LightsDippedBeam), true)]
    [InlineData("lights/side", "true", nameof(TelemetrySnapshot.LightsSide), true)]
    // Online / availability and the active journey
    [InlineData("available", "online", nameof(TelemetrySnapshot.IsAvailable), true)]
    [InlineData("available", "offline", nameof(TelemetrySnapshot.IsAvailable), false)]
    [InlineData("drivetrain/currentJourney/distance", "12.3", nameof(TelemetrySnapshot.CurrentJourneyDistance), 12.3)]
    // Charging session
    [InlineData("drivetrain/chargingType", "AC", nameof(TelemetrySnapshot.ChargingType), "AC")]
    [InlineData("drivetrain/chargingCableLock", "true", nameof(TelemetrySnapshot.ChargingCableLock), true)]
    [InlineData("drivetrain/remainingChargingTime", "45", nameof(TelemetrySnapshot.RemainingChargingTime), 45)]
    [InlineData("drivetrain/lastChargeEndingPower", "7.2", nameof(TelemetrySnapshot.LastChargeEndingPower), 7.2)]
    [InlineData("bms/chargeStatus", "charging", nameof(TelemetrySnapshot.BmsChargeStatus), "charging")]
    [InlineData("ccu/onboardChargerPlugStatus", "1", nameof(TelemetrySnapshot.OnboardChargerPlugStatus), 1)]
    [InlineData("ccu/offboardChargerPlugStatus", "2", nameof(TelemetrySnapshot.OffboardChargerPlugStatus), 2)]
    // Onboard charger
    [InlineData("obc/current", "16.0", nameof(TelemetrySnapshot.ObcCurrent), 16.0)]
    [InlineData("obc/voltage", "230.0", nameof(TelemetrySnapshot.ObcVoltage), 230.0)]
    [InlineData("obc/powerSinglePhase", "3680.0", nameof(TelemetrySnapshot.ObcPowerSinglePhase), 3680.0)]
    [InlineData("obc/powerThreePhase", "11000.0", nameof(TelemetrySnapshot.ObcPowerThreePhase), 11000.0)]
    // Battery heating
    [InlineData("drivetrain/batteryHeating", "true", nameof(TelemetrySnapshot.BatteryHeating), true)]
    [InlineData("drivetrain/batteryHeatingSchedule/mode", "timed", nameof(TelemetrySnapshot.BatteryHeatingScheduleMode), "timed")]
    [InlineData("drivetrain/batteryHeatingSchedule/startTime", "07:00", nameof(TelemetrySnapshot.BatteryHeatingScheduleStartTime), "07:00")]
    // Daily efficiency stats
    [InlineData("drivetrain/mileageOfTheDay", "45.2", nameof(TelemetrySnapshot.MileageOfTheDay), 45.2)]
    [InlineData("drivetrain/powerUsageOfDay", "8.5", nameof(TelemetrySnapshot.PowerUsageOfDay), 8.5)]
    [InlineData("drivetrain/mileageSinceLastCharge", "120.0", nameof(TelemetrySnapshot.MileageSinceLastCharge), 120.0)]
    [InlineData("drivetrain/powerUsageSinceLastCharge", "22.3", nameof(TelemetrySnapshot.PowerUsageSinceLastCharge), 22.3)]
    public void ApplyMessage_KnownSubtopic_SetsItsField(string subtopic, string payload, string field, object expected)
    {
        var snapshot = new TelemetrySnapshot();

        Assert.True(TelemetryMapper.ApplyMessage(snapshot, subtopic, payload));
        Assert.Equal(expected, Read(snapshot, field));
    }

    [Fact]
    public void ApplyMessage_UnknownSubtopic_ReturnsFalseAndLeavesSnapshotUnchanged()
    {
        var snapshot = new TelemetrySnapshot();

        Assert.False(TelemetryMapper.ApplyMessage(snapshot, "unknown/topic", "42"));
        Assert.Null(snapshot.FuelLevelPercent);
        Assert.Null(snapshot.IsLocked);
    }

    [Fact]
    public void ApplyMessage_Range_SetsElectricRangeKmNotFuelRange()
    {
        var snapshot = new TelemetrySnapshot();
        var result = TelemetryMapper.ApplyMessage(snapshot, "drivetrain/range", "243.5");
        Assert.True(result);
        Assert.Equal(243.5, snapshot.ElectricRangeKm);
        Assert.Null(snapshot.FuelRangeKm);
    }

    // ── Payload parsing ───────────────────────────────────────────────────────

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

    [Theory]
    [InlineData("drivetrain/fuelLevel", "notanumber", nameof(TelemetrySnapshot.FuelLevelPercent))]
    [InlineData("location/latitude", "", nameof(TelemetrySnapshot.Latitude))]
    public void ApplyMessage_UnreadableNumber_SetsNull(string subtopic, string payload, string field)
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, subtopic, payload);
        Assert.Null(Read(snapshot, field));
    }

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

    [Fact]
    public void ApplyMessage_ChargingLastEnd_ConvertsFromUnixEpoch()
    {
        var snapshot = new TelemetrySnapshot();
        // 2024-01-01 00:00:00 UTC = 1704067200 seconds
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/charging/lastEnd", "1704067200");
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), snapshot.ChargingLastEndAt);
    }

    // ── Compound JSON payloads ────────────────────────────────────────────────

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
    public void ApplyMessage_CurrentJourneyJson_ExtractsDistance()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/currentJourney", """{"distance":8.5,"other":"data"}""");
        Assert.Equal(8.5, snapshot.CurrentJourneyDistance);
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
    public void ApplyMessage_BatteryHeatingScheduleJson_ExtractsFields()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/batteryHeatingSchedule",
            """{"mode":"timed","startTime":"07:30"}""");
        Assert.Equal("timed", snapshot.BatteryHeatingScheduleMode);
        Assert.Equal("07:30", snapshot.BatteryHeatingScheduleStartTime);
    }

    [Theory]
    [InlineData("location/position")]
    [InlineData("drivetrain/currentJourney")]
    [InlineData("drivetrain/chargingSchedule")]
    [InlineData("drivetrain/batteryHeatingSchedule")]
    public void ApplyMessage_MalformedJson_ReturnsFalse(string subtopic)
    {
        Assert.False(TelemetryMapper.ApplyMessage(new TelemetrySnapshot(), subtopic, "{bad json"));
    }
}
