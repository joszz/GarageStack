using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class VehicleTypeHelperTests
{
    private static Vehicle Car(string? model, params (string Key, string Value)[] config) => new()
    {
        Vin = "FAKEVN00000000001",
        Model = model,
        ConfigJson = config.Length == 0 ? null : JsonSerializer.Serialize(config.ToDictionary(c => c.Key, c => c.Value)),
    };

    // Series codes, BType values and model names as real cars report them (gateway and
    // mg-saic-ha issue threads, plus an HS Hybrid+ owner's configuration). Years and colours,
    // which the gateway appends to the model, are placeholders.
    [Theory]
    [InlineData("AS33HEV S", "0", "HS 2024 BLACK", "hev")] // MG HS Hybrid+
    [InlineData("ZP22 EU", "0", "MG3 2024 RED", "hev")] // MG3 Hybrid+
    [InlineData("AS33P S", "2", "HS SUPER HYBRID 2025 GREY", "phev")] // MG HS PHEV
    [InlineData("IS31P L", "2", "MG S9 PHEV 2025 WHITE", "phev")] // MG S9 PHEV
    [InlineData("EH32 S", "1", "MG4 ELECTRIC 2023 BLUE", "bev")] // MG4
    [InlineData("AH4EM L", "2", "MG4 EV URBAN 2025 GREEN", "bev")] // MG4 Urban
    public void GetVehicleType_RealCars(string series, string batteryType, string model, string expected)
    {
        var car = Car(model, ("hw_version", series), ("BType", batteryType));

        Assert.Equal(expected, VehicleTypeHelper.GetVehicleType(car));
    }

    [Fact]
    public void GetVehicleType_OlderCarWithUpperCaseCode()
    {
        // A 2022 ZS EV sends BTYPE, but its series (ZS EV L) already says EV, so this one doesn't.
        var car = Car("XX1 2022 WHITE", ("hw_version", "XX1 L"), ("BTYPE", "1"));

        Assert.Equal("bev", VehicleTypeHelper.GetVehicleType(car));
    }

    [Fact]
    public void GetVehicleType_SeriesThatNamesTheDrivetrain_WinsOverTheCodes()
    {
        var car = Car("ZS EV 2022 WHITE", ("hw_version", "ZS EV L"), ("BType", "0"));

        Assert.Equal("bev", VehicleTypeHelper.GetVehicleType(car));
    }

    [Fact]
    public void GetVehicleType_PhevSeries_IsNotReadAsHevOrBev()
    {
        var car = Car(null, ("hw_version", "XX1 PHEV"));

        Assert.Equal("phev", VehicleTypeHelper.GetVehicleType(car));
    }

    [Fact]
    public void GetVehicleType_PlugInWithHybridOnlyInTheModelName_IsPhev()
    {
        var car = Car("XX2 PLUG-IN HYBRID 2022 BLUE", ("hw_version", "XX2 L"), ("BType", "1"));

        Assert.Equal("phev", VehicleTypeHelper.GetVehicleType(car));
    }

    [Fact]
    public void GetVehicleType_IgnoresTheUnrelatedBatteryCode()
    {
        // BATTERY is a different code from BType and is 0 on some BEVs, such as the MG4 Urban.
        var car = Car("MG4 ELECTRIC 2023 BLUE", ("hw_version", "EH32 S"), ("BATTERY", "0"), ("BType", "1"));

        Assert.Equal("bev", VehicleTypeHelper.GetVehicleType(car));
    }

    [Theory]
    [InlineData(null)] // nothing received from the gateway yet
    [InlineData("""{"hw_version":"EH32 S"}""")] // series known, configuration codes not yet
    [InlineData("not json")]
    public void GetVehicleType_WithoutEnoughToGoOn_IsUnknown(string? configJson)
    {
        var car = new Vehicle { Vin = "FAKEVN00000000001", ConfigJson = configJson };

        Assert.Equal("unknown", VehicleTypeHelper.GetVehicleType(car));
    }
}
