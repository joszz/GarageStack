namespace GarageStack.Core.Models;

/// <summary>
/// What the Demo Controls change on one demo car. Every field is optional: a field left out keeps
/// its value. Metric, like the telemetry it stands in for: temperatures in °C, tyres in bar, the
/// 12 V battery in volts, the charger's power in kW. ChargingType takes "AC" or "DC". Driving true
/// puts the car on the road (engine on, unplugged, moving), false parks it. Starting a charge plugs
/// the cable in, and pulling the cable ends the charge.
/// </summary>
public record DemoStatusOverrideDto(
    bool? IsLocked,
    bool? EngineRunning,
    bool? ClimateOn,
    bool? DriverDoorOpen,
    bool? PassengerDoorOpen,
    bool? RearLeftDoorOpen,
    bool? RearRightDoorOpen,
    bool? TrunkOpen,
    bool? BonnetOpen,
    bool? DriverWindowOpen,
    bool? PassengerWindowOpen,
    bool? RearLeftWindowOpen,
    bool? RearRightWindowOpen,
    bool? ChargerConnected,
    bool? IsCharging,
    bool? LightsMainBeam,
    bool? LightsDippedBeam,
    bool? LightsSide,
    double? EvSocPercent,
    double? InteriorTemperature,
    double? ExteriorTemperature,
    double? Speed,
    bool? IsAvailable = null,
    double? FuelLevelPercent = null,
    double? TyrePressureFrontLeft = null,
    double? TyrePressureFrontRight = null,
    double? TyrePressureRearLeft = null,
    double? TyrePressureRearRight = null,
    double? BatteryVoltage = null,
    string? ChargingType = null,
    double? ChargingPowerKw = null,
    bool? Driving = null
);
