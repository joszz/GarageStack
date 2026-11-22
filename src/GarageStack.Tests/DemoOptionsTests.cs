using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data.Demo;
using Microsoft.Extensions.Configuration;

namespace GarageStack.Tests;

public class DemoOptionsTests
{
    private static DemoOptions From(string? secondVehicle) =>
        DemoOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DemoOptions.SecondVehicleKey] = secondVehicle })
            .Build());

    [Fact]
    public void From_Unset_KeepsTheOneCar()
    {
        var demo = From(null);

        Assert.False(demo.SecondVehicle);
        Assert.Equal([DemoVehicleRepository.DemoVehicle.Vin], demo.Vehicles.Select(v => v.Vin));
    }

    [Fact]
    public void From_True_AddsTheSecondCar()
    {
        var demo = From("true");

        Assert.Equal(
            [DemoVehicleRepository.DemoVehicle.Vin, DemoVehicleRepository.SecondDemoVehicle.Vin],
            demo.Vehicles.Select(v => v.Vin));
    }

    [Fact]
    public async Task Repository_ListsAndFindsTheConfiguredCars()
    {
        var oneCar = new DemoVehicleRepository(From(null));
        var twoCars = new DemoVehicleRepository(From("true"));

        Assert.Single(await oneCar.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Null(await oneCar.GetByVinAsync(DemoVehicleRepository.SecondDemoVehicle.Vin, TestContext.Current.CancellationToken));
        Assert.Equal(2, (await twoCars.GetAllAsync(TestContext.Current.CancellationToken)).Count);
        Assert.NotNull(await twoCars.GetByVinAsync(DemoVehicleRepository.SecondDemoVehicle.Vin, TestContext.Current.CancellationToken));
    }

    // Only the second car is changed: the first is shared with the host tests running alongside.
    [Fact]
    public async Task Repository_KeepsSettingsForTheCarTheyAreFor()
    {
        var ct = TestContext.Current.CancellationToken;
        var twoCars = new DemoVehicleRepository(From("true"));
        var second = DemoVehicleRepository.SecondDemoVehicle;
        try
        {
            await twoCars.SetSettingsAsync(second.Id, new VehicleSettings { HvBatteryCapacityKwh = 17.6 }, ct);

            Assert.Equal(17.6, (await twoCars.GetByVinAsync(second.Vin, ct))!.Settings?.HvBatteryCapacityKwh);
        }
        finally
        {
            second.Settings = null;
        }
    }

    // It reports what a real plug-in hybrid does (BType 2), not a plain hybrid's 0.
    [Fact]
    public void SecondCar_IsAPlugInHybridWithoutAChargeTarget()
    {
        var car = DemoVehicleRepository.SecondDemoVehicle;

        Assert.Equal("phev", VehicleTypeHelper.GetVehicleType(car));
        Assert.False(VehicleTypeHelper.SupportsTargetSoc(car));
    }
}
