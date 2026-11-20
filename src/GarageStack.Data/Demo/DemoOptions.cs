using GarageStack.Core.Configuration;
using GarageStack.Core.Models;
using Microsoft.Extensions.Configuration;

namespace GarageStack.Data.Demo;

/// <summary>
/// What the demo shows beyond its one car, read from configuration as DEMO_MODE is.
/// DEMO_SECOND_VEHICLE=true adds a plug-in hybrid for trying the car picker; the public demo
/// leaves it off and keeps its one car.
/// </summary>
public sealed record DemoOptions(bool SecondVehicle)
{
    public const string SecondVehicleKey = "DEMO_SECOND_VEHICLE";

    public static DemoOptions From(IConfiguration configuration) =>
        new(configuration.SwitchOrDefault(SecondVehicleKey, fallback: false));

    /// <summary>The demo's cars: the ZS EV, and the plug-in hybrid when it is switched on.</summary>
    public IReadOnlyList<Vehicle> Vehicles =>
        SecondVehicle
            ? [DemoVehicleRepository.DemoVehicle, DemoVehicleRepository.SecondDemoVehicle]
            : [DemoVehicleRepository.DemoVehicle];
}
