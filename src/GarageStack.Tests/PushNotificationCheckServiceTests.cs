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
        TyrePressureThresholds? thresholds = null, FakePushSender? push = null) =>
        new(
            NullLogger<PushNotificationCheckService>.Instance,
            new FakeServiceScopeFactory(),
            push ?? new FakePushSender(),
            thresholds ?? TyrePressureThresholds.Default,
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
    // CheckChargingComplete: BEV/PHEV only, transition detection
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
}
