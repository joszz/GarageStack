using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Services;

public class PushNotificationCheckService(
    ILogger<PushNotificationCheckService> logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender,
    TyrePressureThresholds tyrePressureThresholds,
    IStringLocalizer<NotificationStrings> strings) : PeriodicBackgroundService(logger)
{
    private readonly NotificationCooldownGate _cooldownGate = new(TimeSpan.FromHours(1));
    internal readonly VinStateTracker<bool?> _isChargingTracker = new();
    private readonly TimeSpan _parkingGrace = TimeSpan.FromMinutes(10);

    // The positions each check reads, in the order they are listed in an alert body. Tyres keep
    // their conventional abbreviations; door and window positions are localized by resource key.
    private static readonly (string Label, Func<TelemetrySnapshot, double?> Read)[] TyrePositions =
    [
        ("FL", s => s.TyrePressureFrontLeft),
        ("FR", s => s.TyrePressureFrontRight),
        ("RL", s => s.TyrePressureRearLeft),
        ("RR", s => s.TyrePressureRearRight),
    ];

    private static readonly (string ResourceKey, Func<TelemetrySnapshot, bool?> Read)[] DoorPositions =
    [
        ("PositionDriver", s => s.DriverDoorOpen),
        ("PositionPassenger", s => s.PassengerDoorOpen),
        ("PositionRearLeft", s => s.RearLeftDoorOpen),
        ("PositionRearRight", s => s.RearRightDoorOpen),
        ("PositionBoot", s => s.TrunkOpen),
        ("PositionBonnet", s => s.BonnetOpen),
    ];

    private static readonly (string ResourceKey, Func<TelemetrySnapshot, bool?> Read)[] WindowPositions =
    [
        ("PositionDriver", s => s.DriverWindowOpen),
        ("PositionPassenger", s => s.PassengerWindowOpen),
        ("PositionRearLeft", s => s.RearLeftWindowOpen),
        ("PositionRearRight", s => s.RearRightWindowOpen),
    ];

    protected override string Name => "Push notification check";

    protected override TimeSpan InitialDelay => TimeSpan.FromMinutes(5);

    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var vehicleRepo = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();
        // Still needed directly for the notification-history lookup behind the cooldown gate.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vehicles = await vehicleRepo.GetAllAsync(ct);

        foreach (var vehicle in vehicles)
        {
            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            if (snapshot is null) continue;

            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicle);
            var alerts = new List<(string key, string title, string body)>();
            CheckTyrePressure(snapshot, alerts);
            CheckEvSoc(snapshot, vehicleType, alerts);
            CheckChargingComplete(snapshot, vehicle.Vin, vehicleType, alerts);

            // The MQTT consumer records when the engine last stopped (it also sends the
            // engine-start push, the moment the car reports it). A car parked a moment ago is
            // being unloaded, not left open, so the parked alerts wait out a grace period.
            var withinParkingGrace = IsWithinParkingGrace(vehicle.LastParkedAt, DateTime.UtcNow);

            CheckUnlockedWhileParked(snapshot, alerts, withinParkingGrace);
            CheckDoorsOpenWhileParked(snapshot, alerts, withinParkingGrace);
            CheckWindowsOpenWhileParked(snapshot, alerts, withinParkingGrace);

            foreach (var (key, title, body) in alerts)
            {
                // VehicleId is included in the DB check so one vehicle's alert cannot suppress
                // another vehicle's same-category alert.
                var shouldNotify = await _cooldownGate.ShouldNotifyAsync(vehicle.Vin, key, cutoff =>
                    db.WasNotificationSentSinceAsync(key, vehicle.Id, cutoff, ct));
                if (!shouldNotify) continue;

                await pushSender.SendToAllAsync(title, body, ct, key, vehicle.Id);
                logger.LogInformation("Push sent: {Vin}/{Key} - {Title}", LogRedaction.Vin(vehicle.Vin), key, title);
            }
        }
    }

    internal void CheckTyrePressure(TelemetrySnapshot s, List<(string, string, string)> alerts)
    {
        var low = TyrePositions
            .Where(p => p.Read(s) is { } bar && bar < tyrePressureThresholds.LowBar)
            .Select(p => p.Label)
            .ToList();
        if (low.Count > 0)
            alerts.Add((NotificationCategories.LowTyre, strings["LowTyreTitle"], strings["LowTyreBody", string.Join(", ", low)]));

        var high = TyrePositions
            .Where(p => p.Read(s) is { } bar && bar > tyrePressureThresholds.HighBar)
            .Select(p => p.Label)
            .ToList();
        if (high.Count > 0)
            alerts.Add((NotificationCategories.HighTyre, strings["HighTyreTitle"], strings["HighTyreBody", string.Join(", ", high)]));
    }

    private void CheckEvSoc(TelemetrySnapshot s, string vehicleType, List<(string, string, string)> alerts)
    {
        if (!VehicleTypeHelper.CanCharge(vehicleType)) return;
        if (s.EvSocPercent is not null && s.EvSocPercent < 20)
            alerts.Add((NotificationCategories.LowEv, strings["LowEvTitle"], strings["LowEvBody", $"{s.EvSocPercent:F0}"]));
    }

    internal void CheckChargingComplete(TelemetrySnapshot s, string vin, string vehicleType, List<(string, string, string)> alerts)
    {
        if (!VehicleTypeHelper.CanCharge(vehicleType)) return;
        if (s.IsCharging is null) return;

        var current = s.IsCharging.Value;
        var hadPrevious = _isChargingTracker.TryUpdate(vin, current, out var previous);

        if (BoolTransitionDetector.Detect(hadPrevious, previous, current) != StateTransition.TurnedOff)
            return;

        // Charging finished while cable is still connected (session complete, not unplugged mid-charge)
        if (s.ChargerConnected == true)
        {
            var body = s.EvSocPercent is not null
                ? strings["ChargingCompleteBodyWithSoc", $"{s.EvSocPercent:F0}"]
                : strings["ChargingCompleteBody"];
            alerts.Add((NotificationCategories.ChargingComplete, strings["ChargingCompleteTitle"], body));
        }
    }

    internal bool IsWithinParkingGrace(DateTime? lastParkedAt, DateTime now) =>
        lastParkedAt is { } parkedAt && now - parkedAt < _parkingGrace;

    private static bool IsParked(TelemetrySnapshot s)
        => s.EngineRunning == false;

    internal void CheckUnlockedWhileParked(TelemetrySnapshot s, List<(string, string, string)> alerts, bool withinParkingGrace)
    {
        if (!IsParked(s) || withinParkingGrace) return;
        if (s.IsLocked is false)
            alerts.Add((NotificationCategories.UnlockedParked, strings["UnlockedParkedTitle"], strings["UnlockedParkedBody"]));
    }

    internal void CheckDoorsOpenWhileParked(TelemetrySnapshot s, List<(string, string, string)> alerts, bool withinParkingGrace)
    {
        if (!IsParked(s) || withinParkingGrace) return;

        var open = OpenPositions(s, DoorPositions);
        if (open.Count > 0)
            alerts.Add((NotificationCategories.DoorsOpenParked, strings["DoorsOpenTitle"], strings["DoorsOpenBody", string.Join(", ", open)]));
    }

    internal void CheckWindowsOpenWhileParked(TelemetrySnapshot s, List<(string, string, string)> alerts, bool withinParkingGrace)
    {
        if (!IsParked(s) || withinParkingGrace) return;

        var open = OpenPositions(s, WindowPositions);
        if (open.Count > 0)
            alerts.Add((NotificationCategories.WindowsOpenParked, strings["WindowsOpenTitle"], strings["WindowsOpenBody", string.Join(", ", open)]));
    }

    private List<string> OpenPositions(TelemetrySnapshot s, (string ResourceKey, Func<TelemetrySnapshot, bool?> Read)[] positions) =>
        positions
            .Where(p => p.Read(s) == true)
            .Select(p => strings[p.ResourceKey].Value)
            .ToList();
}
