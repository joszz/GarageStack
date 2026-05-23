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

    [JsonIgnore]
    public string? RawTopic { get; set; }
    [JsonIgnore]
    public string? RawPayload { get; set; }
}
