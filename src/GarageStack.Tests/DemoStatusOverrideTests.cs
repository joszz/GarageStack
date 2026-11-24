using GarageStack.Core.Models;
using GarageStack.Data.Demo;

namespace GarageStack.Tests;

/// <summary>
/// What the Demo Controls change on a demo car, and how the car's state stays in one piece: a
/// charge needs the cable and shows a power, driving unplugs the car, parking stops it.
/// </summary>
public class DemoStatusOverrideTests
{
    private static readonly int Zs = DemoVehicleRepository.DemoVehicle.Id;
    private static readonly int Hs = DemoVehicleRepository.SecondDemoVehicle.Id;

    private static readonly DemoStatusOverrideDto Nothing = new(
        null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static async Task<TelemetrySnapshot> AfterAsync(DemoTelemetryRepository telemetry, int vehicleId, DemoStatusOverrideDto change)
    {
        telemetry.ApplyOverride(vehicleId, change);
        return (await telemetry.GetMergedLatestAsync(vehicleId, TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task StartingACharge_PlugsTheCableIn_AndShowsWhatTheWallBoxGives()
    {
        var telemetry = new DemoTelemetryRepository();

        var zs = await AfterAsync(telemetry, Zs, Nothing with { IsCharging = true });

        Assert.Equal((true, true, "CHARGING_1"), (zs.IsCharging, zs.ChargerConnected, zs.BmsChargeStatus));
        Assert.Equal(6.8, zs.ObcPowerSinglePhase);
        Assert.Equal(-6.1, zs.HvPower);
    }

    [Fact]
    public async Task AFastCharge_ShowsItsPowerAtTheBattery_AndThePowerSentWins()
    {
        var telemetry = new DemoTelemetryRepository();

        var fast = await AfterAsync(telemetry, Zs, Nothing with { IsCharging = true, ChargingType = "dc" });
        Assert.Equal(("DC", -75.0, 0.0), (fast.ChargingType, fast.HvPower, fast.ObcPowerSinglePhase));

        var slower = await AfterAsync(telemetry, Zs, Nothing with { ChargingPowerKw = 50 });
        Assert.Equal(-50, slower.HvPower);
    }

    [Fact]
    public async Task PullingTheCable_EndsTheCharge_AndItsPower()
    {
        var telemetry = new DemoTelemetryRepository();
        await AfterAsync(telemetry, Hs, Nothing with { IsCharging = true });

        var hs = await AfterAsync(telemetry, Hs, Nothing with { ChargerConnected = false });

        Assert.Equal((false, false, "UNPLUGGED"), (hs.IsCharging, hs.ChargerConnected, hs.BmsChargeStatus));
        Assert.Equal((0.0, 0.0), (hs.ObcPowerSinglePhase, hs.HvPower));
    }

    [Fact]
    public async Task Driving_UnplugsAndMovesTheParkedCar_AndParkingStopsTheDrivingOne()
    {
        var telemetry = new DemoTelemetryRepository();

        var hs = await AfterAsync(telemetry, Hs, Nothing with { Driving = true });
        Assert.Equal((true, false, 50.0), (hs.EngineRunning, hs.ChargerConnected, hs.Speed));
        // The HS has no trip in progress, so none is counted: the trip log keeps its last trip as finished.
        Assert.Equal(0, hs.CurrentJourneyDistance);

        var zs = await AfterAsync(telemetry, Zs, Nothing with { Driving = false });
        Assert.Equal((false, 0.0, 0.0), (zs.EngineRunning, zs.Speed, zs.CurrentJourneyDistance));

        var again = await AfterAsync(telemetry, Zs, Nothing with { Driving = true, Speed = 120 });
        Assert.Equal(120, again.Speed);
        Assert.True(again.CurrentJourneyDistance > 0);
    }

    [Fact]
    public async Task FuelTyresAndThe12VoltBattery_TakeWhatIsSent_AndTheFuelRangeFollows()
    {
        var telemetry = new DemoTelemetryRepository();

        var hs = await AfterAsync(telemetry, Hs, Nothing with
        {
            FuelLevelPercent = 50,
            TyrePressureFrontLeft = 1.8,
            TyrePressureRearRight = 3.1,
            BatteryVoltage = 11.6,
        });

        Assert.Equal(50, hs.FuelLevelPercent);
        Assert.Equal(272, hs.FuelRangeKm);
        Assert.Equal((1.8, 3.1, 11.6), (hs.TyrePressureFrontLeft, hs.TyrePressureRearRight, hs.BatteryVoltage));
    }

    // The charging rules only run when a change is about charging: closing a door leaves the cable
    // lock as a command set it.
    [Fact]
    public async Task AChangeThatIsNotAboutCharging_LeavesTheChargeAsItWas()
    {
        var telemetry = new DemoTelemetryRepository();
        Assert.Null(telemetry.ApplyCommand(Hs, "charging-cable-lock", "false"));

        var hs = await AfterAsync(telemetry, Hs, Nothing with { DriverDoorOpen = true });

        Assert.Equal((false, true), (hs.ChargingCableLock, hs.ChargerConnected));
    }

    [Fact]
    public async Task TheChargingCommand_ShowsThePowerOfTheCarsOwnWallBox()
    {
        var ct = TestContext.Current.CancellationToken;
        var telemetry = new DemoTelemetryRepository();

        Assert.Null(telemetry.ApplyCommand(Hs, "charging", "true"));

        Assert.Equal(3.6, (await telemetry.GetMergedLatestAsync(Hs, ct))!.ObcPowerSinglePhase);
    }
}
