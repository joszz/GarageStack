using System.Text.Json.Serialization;

namespace GarageStack.Core.Models;

public class TelemetrySnapshot
{
    public long Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public double? FuelLevelPercent { get; set; }
    public double? FuelRangeKm { get; set; }
    public double? OdometerKm { get; set; }

    public bool? IsLocked { get; set; }
    public bool? EngineRunning { get; set; }
    public bool? ClimateOn { get; set; }

    public bool? DriverDoorOpen { get; set; }
    public bool? PassengerDoorOpen { get; set; }
    public bool? RearLeftDoorOpen { get; set; }
    public bool? RearRightDoorOpen { get; set; }
    public bool? TrunkOpen { get; set; }
    public bool? BonnetOpen { get; set; }

    public bool? DriverWindowOpen { get; set; }
    public bool? PassengerWindowOpen { get; set; }
    public bool? RearLeftWindowOpen { get; set; }
    public bool? RearRightWindowOpen { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Speed { get; set; }
    public double? Heading { get; set; }

    public double? BatteryVoltage { get; set; }
    public double? InteriorTemperature { get; set; }
    public double? ExteriorTemperature { get; set; }

    public double? EvSocPercent { get; set; }
    public bool? IsCharging { get; set; }
    public bool? SunRoofOpen { get; set; }
    public double? TyrePressureFrontLeft { get; set; }
    public double? TyrePressureFrontRight { get; set; }
    public double? TyrePressureRearLeft { get; set; }
    public double? TyrePressureRearRight { get; set; }

    public double? MileageOfTheDay { get; set; }
    public double? PowerUsageOfDay { get; set; }
    public double? MileageSinceLastCharge { get; set; }

    // HV drivetrain (hybrid/EV high-voltage system)
    public double? HvVoltage { get; set; }
    public double? HvCurrent { get; set; }
    public double? HvPower { get; set; }
    public double? HvSocKwh { get; set; }
    public double? HvTotalCapacityKwh { get; set; }
    public double? PowerUsageSinceLastCharge { get; set; }
    public bool? ChargerConnected { get; set; }
    public bool? HvBatteryActive { get; set; }

    // Lights
    public bool? LightsMainBeam { get; set; }
    public bool? LightsDippedBeam { get; set; }
    public bool? LightsSide { get; set; }

    // Climate extras
    public double? RemoteTemperature { get; set; }
    public int? HeatedSeatFrontLeft { get; set; }
    public int? HeatedSeatFrontRight { get; set; }
    public bool? RearWindowDefroster { get; set; }

    // Online / availability
    public bool? IsAvailable { get; set; }
    public DateTime? LastVehicleStateAt { get; set; }
    public DateTime? LastChargeStateAt { get; set; }

    // Active journey
    public double? CurrentJourneyDistance { get; set; }

    // Charging session
    public string? ChargingType { get; set; }
    public bool? ChargingCableLock { get; set; }
    public int? RemainingChargingTime { get; set; }
    public string? BmsChargeStatus { get; set; }
    public int? OnboardChargerPlugStatus { get; set; }
    public int? OffboardChargerPlugStatus { get; set; }
    public double? LastChargeEndingPower { get; set; }
    public DateTime? ChargingLastEndAt { get; set; }
    public string? ChargingScheduleMode { get; set; }
    public string? ChargingScheduleStartTime { get; set; }
    public string? ChargingScheduleEndTime { get; set; }

    // OBC (onboard charger)
    public double? ObcCurrent { get; set; }
    public double? ObcVoltage { get; set; }
    public double? ObcPowerSinglePhase { get; set; }
    public double? ObcPowerThreePhase { get; set; }

    // Battery heating
    public bool? BatteryHeating { get; set; }
    public string? BatteryHeatingScheduleMode { get; set; }
    public string? BatteryHeatingScheduleStartTime { get; set; }

    // Location extras
    public double? Elevation { get; set; }

    [JsonIgnore]
    public string? RawTopic { get; set; }

    public TelemetrySnapshot Clone() => (TelemetrySnapshot)MemberwiseClone();
}
