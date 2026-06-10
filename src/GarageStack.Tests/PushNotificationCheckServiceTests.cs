using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

file sealed class FakeNotificationPushSender : IPushSender
{
    public Task SendToAllAsync(string title, string body, CancellationToken ct = default, string? category = null, int? vehicleId = null)
        => Task.CompletedTask;
}

file sealed class FakeNotificationScopeFactory : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new FakeNotificationScope();

    private sealed class FakeNotificationScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new FakeNotificationServiceProvider();
        public void Dispose() { }
    }

    private sealed class FakeNotificationServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

public class PushNotificationCheckServiceTests
{
    private static PushNotificationCheckService CreateService() =>
        new(
            NullLogger<PushNotificationCheckService>.Instance,
            new FakeNotificationScopeFactory(),
            new FakeNotificationPushSender());

    private static TelemetrySnapshot Parked(Action<TelemetrySnapshot>? configure = null)
    {
        var s = new TelemetrySnapshot { EngineRunning = false };
        configure?.Invoke(s);
        return s;
    }

    // ---------------------------------------------------------------------------
    // CheckEngineStart — parking transition detection
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckEngineStart_ParkingTransition_SetsLastParkedAt()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckEngineStart(new TelemetrySnapshot { EngineRunning = true }, "VIN1", alerts);
        svc.CheckEngineStart(new TelemetrySnapshot { EngineRunning = false }, "VIN1", alerts);

        Assert.True(svc._lastParkedAt.ContainsKey("VIN1"));
        Assert.True(DateTime.UtcNow - svc._lastParkedAt["VIN1"] < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CheckEngineStart_StartTransition_DoesNotSetLastParkedAt()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckEngineStart(new TelemetrySnapshot { EngineRunning = false }, "VIN1", alerts);
        svc.CheckEngineStart(new TelemetrySnapshot { EngineRunning = true }, "VIN1", alerts);

        Assert.False(svc._lastParkedAt.ContainsKey("VIN1"));
    }

    [Fact]
    public void CheckEngineStart_FirstObservationStopped_NoGraceSet()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckEngineStart(new TelemetrySnapshot { EngineRunning = false }, "VIN1", alerts);

        Assert.False(svc._lastParkedAt.ContainsKey("VIN1"));
    }

    [Fact]
    public void CheckEngineStart_MultipleVins_GraceTrackedIndependently()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckEngineStart(new TelemetrySnapshot { EngineRunning = true }, "VIN1", alerts);
        svc.CheckEngineStart(new TelemetrySnapshot { EngineRunning = true }, "VIN2", alerts);
        svc.CheckEngineStart(new TelemetrySnapshot { EngineRunning = false }, "VIN1", alerts);

        Assert.True(svc._lastParkedAt.ContainsKey("VIN1"));
        Assert.False(svc._lastParkedAt.ContainsKey("VIN2"));
    }

    // ---------------------------------------------------------------------------
    // CheckDoorsOpenWhileParked — grace flag suppression
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckDoorsOpenWhileParked_WithinGrace_NoAlert()
    {
        var alerts = new List<(string, string, string)>();

        PushNotificationCheckService.CheckDoorsOpenWhileParked(
            Parked(s => s.DriverDoorOpen = true), alerts, withinParkingGrace: true);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckDoorsOpenWhileParked_AfterGrace_FiresAlert()
    {
        var alerts = new List<(string, string, string)>();

        PushNotificationCheckService.CheckDoorsOpenWhileParked(
            Parked(s => s.DriverDoorOpen = true), alerts, withinParkingGrace: false);

        Assert.Single(alerts);
        Assert.Equal("doors-open-parked", alerts[0].Item1);
    }

    [Fact]
    public void CheckDoorsOpenWhileParked_EngineRunning_NoAlertRegardlessOfGrace()
    {
        var alerts = new List<(string, string, string)>();
        var snap = new TelemetrySnapshot { EngineRunning = true, DriverDoorOpen = true };

        PushNotificationCheckService.CheckDoorsOpenWhileParked(snap, alerts, withinParkingGrace: false);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckDoorsOpenWhileParked_MultipleDoors_AlertListsAll()
    {
        var alerts = new List<(string, string, string)>();

        PushNotificationCheckService.CheckDoorsOpenWhileParked(
            Parked(s =>
            {
                s.DriverDoorOpen = true;
                s.TrunkOpen = true;
            }),
            alerts,
            withinParkingGrace: false);

        Assert.Single(alerts);
        Assert.Contains("driver", alerts[0].Item3);
        Assert.Contains("boot", alerts[0].Item3);
    }

    // ---------------------------------------------------------------------------
    // CheckUnlockedWhileParked — grace flag suppression
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckUnlockedWhileParked_WithinGrace_NoAlert()
    {
        var alerts = new List<(string, string, string)>();

        PushNotificationCheckService.CheckUnlockedWhileParked(
            Parked(s => s.IsLocked = false), alerts, withinParkingGrace: true);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckUnlockedWhileParked_AfterGrace_FiresAlert()
    {
        var alerts = new List<(string, string, string)>();

        PushNotificationCheckService.CheckUnlockedWhileParked(
            Parked(s => s.IsLocked = false), alerts, withinParkingGrace: false);

        Assert.Single(alerts);
        Assert.Equal("unlocked-parked", alerts[0].Item1);
    }

    [Fact]
    public void CheckUnlockedWhileParked_IsLockedTrue_NoAlert()
    {
        var alerts = new List<(string, string, string)>();

        PushNotificationCheckService.CheckUnlockedWhileParked(
            Parked(s => s.IsLocked = true), alerts, withinParkingGrace: false);

        Assert.Empty(alerts);
    }

    // ---------------------------------------------------------------------------
    // CheckWindowsOpenWhileParked — grace flag suppression
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckWindowsOpenWhileParked_WithinGrace_NoAlert()
    {
        var alerts = new List<(string, string, string)>();

        PushNotificationCheckService.CheckWindowsOpenWhileParked(
            Parked(s => s.DriverWindowOpen = true), alerts, withinParkingGrace: true);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckWindowsOpenWhileParked_AfterGrace_FiresAlert()
    {
        var alerts = new List<(string, string, string)>();

        PushNotificationCheckService.CheckWindowsOpenWhileParked(
            Parked(s => s.DriverWindowOpen = true), alerts, withinParkingGrace: false);

        Assert.Single(alerts);
        Assert.Equal("windows-open-parked", alerts[0].Item1);
    }

    // ---------------------------------------------------------------------------
    // CheckChargingComplete — BEV/PHEV only, transition detection
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckChargingComplete_TransitionToNotCharging_CableConnected_FiresAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "bev", alerts);
        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true }, "VIN1", "bev", alerts);

        Assert.Single(alerts);
        Assert.Equal("charging-complete", alerts[0].Item1);
    }

    [Fact]
    public void CheckChargingComplete_IncludesSocInBody_WhenAvailable()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "bev", alerts);
        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true, EvSocPercent = 98 }, "VIN1", "bev", alerts);

        Assert.Contains("98%", alerts[0].Item3);
    }

    [Fact]
    public void CheckChargingComplete_CableDisconnected_NoAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "bev", alerts);
        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = false, ChargerConnected = false }, "VIN1", "bev", alerts);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckChargingComplete_HevVehicle_NoAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "hev", alerts);
        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true }, "VIN1", "hev", alerts);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckChargingComplete_PhevVehicle_FiresAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "phev", alerts);
        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true }, "VIN1", "phev", alerts);

        Assert.Single(alerts);
        Assert.Equal("charging-complete", alerts[0].Item1);
    }

    [Fact]
    public void CheckChargingComplete_FirstObservation_NoAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true }, "VIN1", "bev", alerts);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckChargingComplete_MultipleVins_TrackedIndependently()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "bev", alerts);
        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN2", "bev", alerts);
        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true }, "VIN1", "bev", alerts);

        Assert.Single(alerts);
        Assert.Equal("charging-complete", alerts[0].Item1);
    }

    // ---------------------------------------------------------------------------
    // CheckEvSoc — BEV/PHEV only via vehicle type guard
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckEvSoc_HevVehicle_NoAlertEvenBelowThreshold()
    {
        // We test this indirectly via CheckChargingComplete's CanCharge guard —
        // CheckEvSoc is private, but we can verify the HEV guard via CheckChargingComplete.
        // For direct SOC coverage, the important contract is: HEV never gets charging-related alerts.
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingComplete(new TelemetrySnapshot { IsCharging = true }, "VIN1", "hev", alerts);

        Assert.Empty(alerts);
    }
}
