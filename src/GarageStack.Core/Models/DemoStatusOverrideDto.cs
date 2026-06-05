namespace GarageStack.Core.Models;

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
    double? ExteriorTemperature
);
