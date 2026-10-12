using System.Globalization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// FakePushSender / FakeServiceScopeFactory live in WorkerTestFakes.cs.

public class PushNotificationCheckServiceTests
{
    private static PushNotificationCheckService CreateService(
        TyrePressureThresholds? thresholds = null, FakePushSender? push = null, AlertThresholds? alertThresholds = null) =>
        new(
            NullLogger<PushNotificationCheckService>.Instance,
            new FakeServiceScopeFactory(),
            push ?? new FakePushSender(),
            thresholds ?? TyrePressureThresholds.Default,
            alertThresholds ?? AlertThresholds.Default,
            WorkerLocalizer.Notifications());

    private static TelemetrySnapshot Parked(Action<TelemetrySnapshot>? configure = null)
    {
        var s = new TelemetrySnapshot { EngineRunning = false };
        configure?.Invoke(s);
        return s;
    }

    private static TelemetrySnapshot Locked(Action<TelemetrySnapshot>? configure = null) =>
        Parked(s => { s.IsLocked = true; configure?.Invoke(s); });

    private static TelemetrySnapshot Unlocked(Action<TelemetrySnapshot>? configure = null) =>
        Parked(s => { s.IsLocked = false; configure?.Invoke(s); });

    private static readonly DateTime ParkedAt = new(2026, 9, 23, 14, 57, 0, DateTimeKind.Utc);

    // The gateway's polls after the car parks, two and a half minutes apart. Written oldest first
    // and handed over newest first, the way the repository returns them.
    private static List<StatusReading> Polls(params TelemetrySnapshot[] oldestFirst) =>
        [.. oldestFirst.Select((state, i) => new StatusReading(ParkedAt.AddSeconds(150 * i), state)).Reverse()];

    // ---------------------------------------------------------------------------
    // Parking grace: the MQTT consumer records when the engine stopped
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParkingGrace_CoversTheTenMinutesAfterTheEngineStopped()
    {
        var svc = CreateService();
        var now = DateTime.UtcNow;

        Assert.True(svc.IsWithinParkingGrace(now.AddMinutes(-9), now));
        Assert.False(svc.IsWithinParkingGrace(now.AddMinutes(-11), now));
    }

    [Fact]
    public void ParkingGrace_WithoutARecordedStop_DoesNotHoldAlertsBack()
    {
        Assert.False(CreateService().IsWithinParkingGrace(null, DateTime.UtcNow));
    }

    // ---------------------------------------------------------------------------
    // Parked checks: a problem counts once the polls keep showing it
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckUnlockedWhileParked_UnlockedOnEveryPoll_FiresAlert()
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckUnlockedWhileParked(Polls(Unlocked(), Unlocked(), Unlocked(), Unlocked(), Unlocked()), alerts);

        Assert.Equal("unlocked-parked", Assert.Single(alerts).Item1);
    }

    [Fact]
    public void CheckUnlockedWhileParked_Locked_NoAlert()
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckUnlockedWhileParked(Polls(Locked(), Locked(), Locked(), Locked()), alerts);

        Assert.Empty(alerts);
    }

    // Locked on the way out, then caught on the last poll by the driver coming back for something:
    // the car sleeps on that poll until the next drive.
    [Fact]
    public void ParkedChecks_DriverBackAtTheCarOnTheLastPoll_NoAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();
        var polls = Polls(
            Unlocked(s => s.DriverDoorOpen = true), Locked(), Locked(), Locked(), Unlocked(s => s.DriverDoorOpen = true));

        svc.CheckUnlockedWhileParked(polls, alerts);
        svc.CheckDoorsOpenWhileParked(polls, alerts);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckDoorsOpenWhileParked_OpenOnEveryPoll_AlertListsTheDoorsOpenNow()
    {
        var alerts = new List<(string, string, string)>();
        void DriverAndBoot(TelemetrySnapshot s) { s.DriverDoorOpen = true; s.TrunkOpen = true; }

        CreateService().CheckDoorsOpenWhileParked(
            Polls(Unlocked(DriverAndBoot), Unlocked(DriverAndBoot), Unlocked(DriverAndBoot), Unlocked(s => s.TrunkOpen = true)),
            alerts);

        var (category, _, body) = Assert.Single(alerts);
        Assert.Equal("doors-open-parked", category);
        Assert.Contains("boot", body);
        Assert.DoesNotContain("driver", body);
    }

    [Fact]
    public void CheckWindowsOpenWhileParked_OpenOnEveryPoll_FiresAlert()
    {
        var alerts = new List<(string, string, string)>();
        void DriverWindow(TelemetrySnapshot s) => s.DriverWindowOpen = true;

        CreateService().CheckWindowsOpenWhileParked(
            Polls(Locked(DriverWindow), Locked(DriverWindow), Locked(DriverWindow), Locked(DriverWindow)), alerts);

        Assert.Equal("windows-open-parked", Assert.Single(alerts).Item1);
    }

    // ---------------------------------------------------------------------------
    // CheckParkedAsync: the grace period, and one alert per poll
    // ---------------------------------------------------------------------------

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // A car left unlocked at parkedAt: the gateway polled it every two and a half minutes for
    // the next ten, then let it sleep.
    private static async Task<(Vehicle Vehicle, DateTime LastPollAt)> LeftUnlockedAsync(
        AppDbContext db, DateTime parkedAt, CancellationToken ct, int polls = 5)
    {
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001", LastParkedAt = parkedAt };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        var pollTimes = Enumerable.Range(0, polls).Select(i => parkedAt.AddSeconds(150 * i)).ToList();
        db.TelemetrySnapshots.AddRange(pollTimes.Select(at => new TelemetrySnapshot
        {
            VehicleId = vehicle.Id,
            RecordedAt = at,
            LastVehicleStateAt = at,
            IsLocked = false,
            EngineRunning = false,
        }));
        await db.SaveChangesAsync(ct);

        return (vehicle, pollTimes[^1]);
    }

    [Fact]
    public async Task CheckParkedAsync_LeftUnlocked_SendsTheAlert()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        await using var db = CreateDb();
        var (vehicle, _) = await LeftUnlockedAsync(db, now.AddHours(-1), ct);
        var push = new FakePushSender();

        await CreateService(push: push).CheckParkedAsync(vehicle, Parked(), new TelemetryRepository(db), db, now, ct);

        Assert.Equal("unlocked-parked", Assert.Single(push.Sent).Category);
    }

    // The overnight case: the alert went out after the car's last poll, over an hour ago, and the
    // car has sent nothing since. The cooldown has run out, but there is nothing new to report.
    [Fact]
    public async Task CheckParkedAsync_AlreadyAlertedOnTheLastPoll_SendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        await using var db = CreateDb();
        var (vehicle, lastPollAt) = await LeftUnlockedAsync(db, now.AddHours(-3), ct);
        db.AppNotifications.Add(new AppNotification
        {
            Title = "Car unlocked",
            Category = NotificationCategories.UnlockedParked,
            VehicleId = vehicle.Id,
            CreatedAt = lastPollAt.AddMinutes(2),
        });
        await db.SaveChangesAsync(ct);
        var push = new FakePushSender();

        await CreateService(push: push).CheckParkedAsync(vehicle, Parked(), new TelemetryRepository(db), db, now, ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task CheckParkedAsync_WithinTheParkingGrace_SendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        await using var db = CreateDb();
        var (vehicle, _) = await LeftUnlockedAsync(db, now.AddMinutes(-8), ct, polls: 4);
        var push = new FakePushSender();

        await CreateService(push: push).CheckParkedAsync(vehicle, Parked(), new TelemetryRepository(db), db, now, ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task CheckParkedAsync_EngineRunning_SendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        await using var db = CreateDb();
        var (vehicle, _) = await LeftUnlockedAsync(db, now.AddHours(-1), ct);
        var push = new FakePushSender();

        await CreateService(push: push).CheckParkedAsync(
            vehicle, new TelemetrySnapshot { EngineRunning = true }, new TelemetryRepository(db), db, now, ct);

        Assert.Empty(push.Sent);
    }

    // ---------------------------------------------------------------------------
    // CheckChargingStopped: BEV/PHEV only, transition detection
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckChargingStopped_TransitionToNotCharging_CableConnected_FiresAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "bev", null, alerts);
        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true }, "VIN1", "bev", null, alerts);

        Assert.Single(alerts);
        Assert.Equal("charging-complete", alerts[0].Item1);
    }

    [Fact]
    public void CheckChargingStopped_IncludesSocInBody_WhenAvailable()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "bev", null, alerts);
        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true, EvSocPercent = 98 }, "VIN1", "bev", null, alerts);

        Assert.Contains("98%", alerts[0].Item3);
    }

    [Fact]
    public void CheckChargingStopped_CableDisconnected_NoAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "bev", null, alerts);
        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = false, ChargerConnected = false }, "VIN1", "bev", null, alerts);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckChargingStopped_HevVehicle_NoAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "hev", null, alerts);
        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true }, "VIN1", "hev", null, alerts);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckChargingStopped_PhevVehicle_FiresAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "phev", null, alerts);
        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true }, "VIN1", "phev", null, alerts);

        Assert.Single(alerts);
        Assert.Equal("charging-complete", alerts[0].Item1);
    }

    [Fact]
    public void CheckChargingStopped_FirstObservation_NoAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true }, "VIN1", "bev", null, alerts);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckChargingStopped_MultipleVins_TrackedIndependently()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "bev", null, alerts);
        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN2", "bev", null, alerts);
        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = false, ChargerConnected = true }, "VIN1", "bev", null, alerts);

        Assert.Single(alerts);
        Assert.Equal("charging-complete", alerts[0].Item1);
    }

    // A charge the car broke off: the stop reason comes from the poll that showed it stop.
    private static List<(string, string, string)> StopCharging(string? stopReason, bool? cableConnected)
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();
        svc.CheckChargingStopped(new TelemetrySnapshot { IsCharging = true, ChargerConnected = true }, "VIN1", "bev", null, alerts);
        svc.CheckChargingStopped(
            new TelemetrySnapshot { IsCharging = false, ChargerConnected = cableConnected, EvSocPercent = 64 }, "VIN1", "bev", stopReason, alerts);
        return alerts;
    }

    [Fact]
    public void CheckChargingStopped_FaultTheCarNames_ReportsItAsInterrupted()
    {
        var (category, _, body) = Assert.Single(StopCharging("CHARGER_PORT_OVER_TEMPERATURE", cableConnected: true));

        Assert.Equal("charging-interrupted", category);
        Assert.Contains("too hot", body);
        Assert.Contains("64%", body);
    }

    // A plug not fully in can read as no cable at all, and that is exactly when it matters.
    [Fact]
    public void CheckChargingStopped_FaultWithTheCableReadAsOut_IsStillReported()
    {
        Assert.Equal("charging-interrupted", Assert.Single(StopCharging("CHARGING_GUN_NOT_PROPERLY_PLUGGED_IN", cableConnected: false)).Item1);
    }

    [Theory]
    [InlineData("OTHER_REASON")]
    [InlineData("UNKNOWN 9")]
    public void CheckChargingStopped_ReasonNotKnownToBeAFault_ReadsAsComplete(string stopReason)
    {
        Assert.Equal("charging-complete", Assert.Single(StopCharging(stopReason, cableConnected: true)).Item1);
    }

    [Fact]
    public void CheckChargingStopped_UnpluggedWithoutAFault_SendsNothing()
    {
        Assert.Empty(StopCharging(null, cableConnected: false));
    }

    // ---------------------------------------------------------------------------
    // CheckTyrePressure: configurable low/high thresholds
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckTyrePressure_BelowDefaultLow_FiresLowAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckTyrePressure(new TelemetrySnapshot { TyrePressureFrontLeft = 2.0 }, alerts);

        Assert.Single(alerts);
        Assert.Equal("low-tyre", alerts[0].Item1);
        Assert.Contains("FL", alerts[0].Item3);
    }

    [Fact]
    public void CheckTyrePressure_AboveDefaultHigh_FiresHighAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckTyrePressure(new TelemetrySnapshot { TyrePressureRearRight = 3.5 }, alerts);

        Assert.Single(alerts);
        Assert.Equal("high-tyre", alerts[0].Item1);
        Assert.Contains("RR", alerts[0].Item3);
    }

    [Fact]
    public void CheckTyrePressure_WithinDefaultRange_NoAlert()
    {
        var svc = CreateService();
        var alerts = new List<(string, string, string)>();

        svc.CheckTyrePressure(new TelemetrySnapshot { TyrePressureFrontLeft = 2.5 }, alerts);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckTyrePressure_UsesConfiguredThresholds_NotHardcodedDefaults()
    {
        var svc = CreateService(new TyrePressureThresholds(LowBar: 2.4, GoodBar: 2.55, HighBar: 2.7));
        var alerts = new List<(string, string, string)>();

        // 2.3 bar is above the default 2.2 low bar (would not alert with defaults), but below
        // the configured 2.4 low bar here.
        svc.CheckTyrePressure(new TelemetrySnapshot { TyrePressureFrontLeft = 2.3 }, alerts);

        Assert.Single(alerts);
        Assert.Equal("low-tyre", alerts[0].Item1);
    }

    // ---------------------------------------------------------------------------
    // CheckEvSoc and CheckFuelLevel: configurable levels, per drivetrain
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckEvSoc_BelowTheConfiguredLevel_FiresAlert()
    {
        var svc = CreateService(alertThresholds: AlertThresholds.Default with { LowEvPercent = 30 });
        var alerts = new List<(string, string, string)>();

        // 25 % is above the default 20 % (no alert with defaults) but below the configured 30 %.
        svc.CheckEvSoc(new TelemetrySnapshot { EvSocPercent = 25 }, "bev", alerts);

        Assert.Equal("low-ev", Assert.Single(alerts).Item1);
    }

    [Fact]
    public void CheckEvSoc_ThresholdZero_NeverFires()
    {
        var svc = CreateService(alertThresholds: AlertThresholds.Default with { LowEvPercent = 0 });
        var alerts = new List<(string, string, string)>();

        svc.CheckEvSoc(new TelemetrySnapshot { EvSocPercent = 0 }, "bev", alerts);

        Assert.Empty(alerts);
    }

    [Theory]
    [InlineData("hev")]
    [InlineData("phev")]
    public void CheckFuelLevel_CarThatBurnsFuel_BelowTheLevel_FiresAlertWithTheLevel(string vehicleType)
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckFuelLevel(new TelemetrySnapshot { FuelLevelPercent = 9 }, vehicleType, alerts);

        var (category, _, body) = Assert.Single(alerts);
        Assert.Equal("low-fuel", category);
        Assert.Contains("9%", body);
    }

    [Fact]
    public void CheckFuelLevel_AtTheLevel_NoAlert()
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckFuelLevel(new TelemetrySnapshot { FuelLevelPercent = 15 }, "hev", alerts);

        Assert.Empty(alerts);
    }

    // A full EV can report a fuel level of zero, and a car not yet detected has no known tank.
    [Theory]
    [InlineData("bev")]
    [InlineData("unknown")]
    public void CheckFuelLevel_CarWithoutAKnownTank_NoAlert(string vehicleType)
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckFuelLevel(new TelemetrySnapshot { FuelLevelPercent = 0 }, vehicleType, alerts);

        Assert.Empty(alerts);
    }

    // ---------------------------------------------------------------------------
    // CheckDataStale and NewestReportAt: the gateway's poll stamps
    // ---------------------------------------------------------------------------

    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewestReportAt_TakesTheLaterOfTheTwoPollStamps()
    {
        var s = new TelemetrySnapshot { RecordedAt = Now, LastVehicleStateAt = Now.AddHours(-5), LastChargeStateAt = Now.AddHours(-3) };

        Assert.Equal(Now.AddHours(-3), PushNotificationCheckService.NewestReportAt(s));
    }

    [Fact]
    public void NewestReportAt_WithoutPollStamps_FallsBackToWhenTheRowWasWritten()
    {
        Assert.Equal(Now, PushNotificationCheckService.NewestReportAt(new TelemetrySnapshot { RecordedAt = Now }));
    }

    [Fact]
    public void CheckDataStale_SilentLongerThanTheThreshold_FiresAlertWithTheHours()
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckDataStale(new TelemetrySnapshot { LastVehicleStateAt = Now.AddHours(-50) }, Now, alerts);

        var (category, _, body) = Assert.Single(alerts);
        Assert.Equal("data-stale", category);
        Assert.Contains("50", body);
    }

    // The gateway polls a sleeping car once a day, so a day of silence is normal.
    [Fact]
    public void CheckDataStale_ADayOfSilence_NoAlert()
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckDataStale(new TelemetrySnapshot { LastVehicleStateAt = Now.AddHours(-25) }, Now, alerts);

        Assert.Empty(alerts);
    }

    // A message replayed on reconnect is written now but carries the poll stamp it was sent with.
    [Fact]
    public void CheckDataStale_ReplayedOldPoll_StillCountsAsSilent()
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckDataStale(new TelemetrySnapshot { RecordedAt = Now, LastVehicleStateAt = Now.AddDays(-3) }, Now, alerts);

        Assert.Equal("data-stale", Assert.Single(alerts).Item1);
    }

    [Fact]
    public void CheckDataStale_ThresholdZero_NeverFires()
    {
        var svc = CreateService(alertThresholds: AlertThresholds.Default with { StaleDataHours = 0 });
        var alerts = new List<(string, string, string)>();

        svc.CheckDataStale(new TelemetrySnapshot { LastVehicleStateAt = Now.AddDays(-30) }, Now, alerts);

        Assert.Empty(alerts);
    }

    // ---------------------------------------------------------------------------
    // CheckAuxBatteryWhileParked: low on poll after poll, not one dip
    // ---------------------------------------------------------------------------

    private static TelemetrySnapshot Volts(double? voltage) => Locked(s => s.BatteryVoltage = voltage);

    [Fact]
    public void CheckAuxBatteryWhileParked_LowOnEveryPoll_FiresAlertWithTheNewestVoltage()
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckAuxBatteryWhileParked(Polls(Volts(11.9), Volts(11.8), Volts(11.8), Volts(11.7)), alerts);

        var (category, _, body) = Assert.Single(alerts);
        Assert.Equal("low-12v", category);
        Assert.Contains(11.7.ToString("0.0", CultureInfo.CurrentUICulture), body);
    }

    [Fact]
    public void CheckAuxBatteryWhileParked_OneLowPoll_NoAlert()
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckAuxBatteryWhileParked(Polls(Volts(12.4), Volts(12.4), Volts(12.4), Volts(11.6)), alerts);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckAuxBatteryWhileParked_NoVoltageReported_NoAlert()
    {
        var alerts = new List<(string, string, string)>();

        CreateService().CheckAuxBatteryWhileParked(Polls(Volts(null), Volts(null), Volts(null), Volts(null)), alerts);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckAuxBatteryWhileParked_UsesTheConfiguredVoltage()
    {
        var svc = CreateService(alertThresholds: AlertThresholds.Default with { AuxBatteryLowVolt = 12.3 });
        var alerts = new List<(string, string, string)>();

        svc.CheckAuxBatteryWhileParked(Polls(Volts(12.2), Volts(12.2), Volts(12.2), Volts(12.2)), alerts);

        Assert.Equal("low-12v", Assert.Single(alerts).Item1);
    }

    // ---------------------------------------------------------------------------
    // CheckLevelsAsync: one alert per report, however long the car sleeps
    // ---------------------------------------------------------------------------

    private static async Task<Vehicle> AddVehicleAsync(AppDbContext db, CancellationToken ct)
    {
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        return vehicle;
    }

    private static TelemetrySnapshot SoftTyreReportedAt(DateTime polledAt) =>
        new() { RecordedAt = polledAt, LastVehicleStateAt = polledAt, TyrePressureFrontLeft = 1.9 };

    // The overnight case: the alert went out after the last poll, over an hour ago, and the car
    // has sent nothing since. The cooldown has run out, but there is nothing new to report.
    [Fact]
    public async Task CheckLevelsAsync_AlreadyAlertedOnTheLastReport_SendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        await using var db = CreateDb();
        var vehicle = await AddVehicleAsync(db, ct);
        var polledAt = now.AddHours(-6);
        db.AppNotifications.Add(new AppNotification
        {
            Title = "Low tyre",
            Category = NotificationCategories.LowTyre,
            VehicleId = vehicle.Id,
            CreatedAt = polledAt.AddMinutes(4),
        });
        await db.SaveChangesAsync(ct);
        var push = new FakePushSender();

        await CreateService(push: push).CheckLevelsAsync(vehicle, SoftTyreReportedAt(polledAt), new TelemetryRepository(db), db, now, ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task CheckLevelsAsync_NewReportStillLow_SendsAgainOnceTheCooldownHasPassed()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        await using var db = CreateDb();
        var vehicle = await AddVehicleAsync(db, ct);
        db.AppNotifications.Add(new AppNotification
        {
            Title = "Low tyre",
            Category = NotificationCategories.LowTyre,
            VehicleId = vehicle.Id,
            CreatedAt = now.AddHours(-3),
        });
        await db.SaveChangesAsync(ct);
        var push = new FakePushSender();

        await CreateService(push: push).CheckLevelsAsync(vehicle, SoftTyreReportedAt(now.AddMinutes(-2)), new TelemetryRepository(db), db, now, ct);

        Assert.Equal("low-tyre", Assert.Single(push.Sent).Category);
    }
}
