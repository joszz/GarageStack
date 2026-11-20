using System.Text.Json;
using GarageStack.Api.Endpoints;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class VehicleSettingsTests
{
    private const string HybridConfig = """{"hw_version":"AS33HEV S","BType":"0"}""";
    private const string ElectricConfig = """{"hw_version":"ZS EV L","BType":"1"}""";

    // What a deployment with every variable set would have: none of these is a built-in default.
    private static readonly VehicleDefaults Configured = new(
        new TyrePressureThresholds(LowBar: 2.3, GoodBar: 2.5, HighBar: 3.0, DropBar: 0.2),
        AlertThresholds.Default with { AuxBatteryLowVolt = 11.8, LowFuelPercent = 10, LowEvPercent = 25 },
        new HvBatteryCapacity(1.83));

    private static Vehicle Car(string config, VehicleSettings? settings = null) =>
        new() { Vin = "FAKEVN00000000001", ConfigJson = config, Settings = settings };

    [Fact]
    public void Resolve_WithoutSettings_GivesExactlyTheDeploymentDefaults()
    {
        var effective = VehicleSettingsResolver.Resolve(Car(HybridConfig), Configured);

        Assert.Equal("hev", effective.VehicleType);
        Assert.Equal(1.83, effective.HvBatteryCapacityKwh);
        Assert.Equal(Configured.Tyres, effective.Tyres);
        Assert.Equal(Configured.Alerts, effective.Alerts);
    }

    [Fact]
    public void Resolve_WithEmptySettings_GivesExactlyTheDeploymentDefaults()
    {
        var effective = VehicleSettingsResolver.Resolve(Car(HybridConfig, new VehicleSettings()), Configured);

        Assert.Equal(VehicleSettingsResolver.Resolve(Car(HybridConfig), Configured), effective);
    }

    [Fact]
    public void Resolve_TakesEachOwnValue_AndTheDefaultForTheRest()
    {
        var effective = VehicleSettingsResolver.Resolve(
            Car(ElectricConfig, new VehicleSettings { HvBatteryCapacityKwh = 64, TyreHighBar = 3.4, LowEvPercent = 15 }),
            Configured);

        Assert.Equal(64, effective.HvBatteryCapacityKwh);
        Assert.Equal(Configured.Tyres with { HighBar = 3.4 }, effective.Tyres);
        Assert.Equal(Configured.Alerts with { LowEvPercent = 15 }, effective.Alerts);
    }

    [Fact]
    public void Resolve_TypeOverride_WinsOverTheDetectedType()
    {
        var effective = VehicleSettingsResolver.Resolve(Car(ElectricConfig, new VehicleSettings { VehicleType = "phev" }), Configured);

        Assert.Equal("phev", effective.VehicleType);
        Assert.Equal("bev", effective.DetectedVehicleType);
    }

    // The settings page offers no battery field on a plain hybrid, so a value from when it was
    // taken for a plug-in does not count: the deployment's stays, as it did before.
    [Fact]
    public void Resolve_PlainHybrid_IgnoresABatterySizeOfItsOwn()
    {
        var effective = VehicleSettingsResolver.Resolve(Car(HybridConfig, new VehicleSettings { HvBatteryCapacityKwh = 20 }), Configured);

        Assert.Equal(1.83, effective.HvBatteryCapacityKwh);
    }

    [Theory]
    [InlineData("tyreLowBar", 0.5)]
    [InlineData("tyreDropBar", -0.1)]
    [InlineData("lowFuelPercent", 101)]
    [InlineData("hvBatteryCapacityKwh", 0)]
    public void Check_ValueOutOfRange_IsRefused(string name, double value)
    {
        var settings = new VehicleSettings();
        VehicleSettingsRules.Numbers.Single(r => r.Name == name).Set(settings, value);

        Assert.Equal(VehicleSettingsRules.OutOfRangeCode, VehicleSettingsRules.Check(Car(ElectricConfig), settings, Configured)?.Code);
    }

    // Zero switches an alert off, as it does in the deployment's variables.
    [Fact]
    public void Check_ZeroAlertLevels_AreTaken()
    {
        var settings = new VehicleSettings { AuxBatteryLowVolt = 0, LowFuelPercent = 0, LowEvPercent = 0, TyreDropBar = 0 };

        Assert.Null(VehicleSettingsRules.Check(Car(ElectricConfig), settings, Configured));
    }

    // 2.6 is above the deployment's good pressure of 2.5, which this car keeps.
    [Fact]
    public void Check_LowAboveTheGoodPressureInEffect_IsRefused()
    {
        var refusal = VehicleSettingsRules.Check(Car(ElectricConfig), new VehicleSettings { TyreLowBar = 2.6 }, Configured);

        Assert.Equal(VehicleSettingsRules.TyreOrderCode, refusal?.Code);
    }

    [Fact]
    public void Check_BatteryOnAPlainHybrid_IsRefused_ButTakenOnceTheTypeIsAPlugIn()
    {
        var battery = new VehicleSettings { HvBatteryCapacityKwh = 20 };

        Assert.Equal(VehicleSettingsRules.BatteryOnHybridCode, VehicleSettingsRules.Check(Car(HybridConfig), battery, Configured)?.Code);
        battery.VehicleType = "phev";
        Assert.Null(VehicleSettingsRules.Check(Car(HybridConfig), battery, Configured));
    }

    [Fact]
    public void Check_UnknownType_IsRefused()
    {
        var refusal = VehicleSettingsRules.Check(Car(ElectricConfig), new VehicleSettings { VehicleType = "unknown" }, Configured);

        Assert.Equal(VehicleSettingsRules.VehicleTypeUnknownCode, refusal?.Code);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ApplyPatch_LeavesOutWhatIsNotSent_AndClearsWhatIsNull()
    {
        var current = new VehicleSettings { TyreLowBar = 2.4, LowFuelPercent = 12 };

        var (updated, refusal) = VehicleSettingsEndpoints.ApplyPatch(Json("""{"lowFuelPercent":null,"vehicleType":"PHEV"}"""), current);

        Assert.Null(refusal);
        Assert.Equal(2.4, updated!.TyreLowBar);
        Assert.Null(updated.LowFuelPercent);
        Assert.Equal("phev", updated.VehicleType);
        Assert.Equal(12, current.LowFuelPercent);
    }

    [Theory]
    [InlineData("""[]""", VehicleSettingsEndpoints.BodyNotObjectCode)]
    [InlineData("""{"chargePrice":0.3}""", VehicleSettingsEndpoints.UnknownFieldCode)]
    [InlineData("""{"tyreLowBar":"2.4"}""", VehicleSettingsEndpoints.WrongTypeCode)]
    [InlineData("""{"vehicleType":1}""", VehicleSettingsEndpoints.WrongTypeCode)]
    public void ApplyPatch_ABodyItCannotRead_IsRefused(string body, string code)
    {
        var (_, refusal) = VehicleSettingsEndpoints.ApplyPatch(Json(body), new VehicleSettings());

        Assert.Equal(code, refusal?.Code);
    }
}
