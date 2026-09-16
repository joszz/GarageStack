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
    IStringLocalizer<NotificationStrings> strings) : BackgroundService
{
    private readonly NotificationCooldownGate _cooldownGate = new(TimeSpan.FromHours(1));
    internal readonly VinStateTracker<bool?> _engineRunningTracker = new();
    internal readonly VinStateTracker<bool?> _isChargingTracker = new();
    internal readonly Dictionary<string, DateTime> _lastParkedAt = new();
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Push notification check service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            try
            {
                await CheckAndNotifyAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error during push notification check");
            }
        }
    }

    private async Task CheckAndNotifyAsync(CancellationToken ct)
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

            // Seed in-memory parking time from DB on first sight of this VIN after a restart
            if (!_lastParkedAt.ContainsKey(vehicle.Vin) && vehicle.LastParkedAt.HasValue)
                _lastParkedAt[vehicle.Vin] = vehicle.LastParkedAt.Value;

            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicle);
            var alerts = new List<(string key, string title, string body)>();
            CheckTyrePressure(snapshot, alerts);
            CheckEvSoc(snapshot, vehicleType, alerts);
            CheckChargingComplete(snapshot, vehicle.Vin, vehicleType, alerts);
            var justParked = CheckEngineStart(snapshot, vehicle.Vin, alerts);
            if (justParked)
                await vehicleRepo.SetLastParkedAtAsync(vehicle.Id, _lastParkedAt[vehicle.Vin], ct);

            var withinParkingGrace = _lastParkedAt.TryGetValue(vehicle.Vin, out var parkedAt)
                && DateTime.UtcNow - parkedAt < _parkingGrace;

            CheckUnlockedWhileParked(snapshot, alerts, withinParkingGrace);
            CheckDoorsOpenWhileParked(snapshot, alerts, withinParkingGrace);
            CheckWindowsOpenWhileParked(snapshot, alerts, withinParkingGrace);

            foreach (var (key, title, body) in alerts)
            {
                // VehicleId is included in the DB check so one vehicle's alert cannot suppress
                // another vehicle's same-category alert; this also lets MQTT-emitted notifications
                // (e.g. engine-start, sent directly from MqttConsumerService) suppress a repeated
                // checker alert for the same category.
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

    internal bool CheckEngineStart(TelemetrySnapshot s, string vin, List<(string, string, string)> alerts)
    {
        if (s.EngineRunning is null) return false;

        var current = s.EngineRunning.Value;
        var hadPrevious = _engineRunningTracker.TryUpdate(vin, current, out var previous);

        // First observation after startup is skipped: no baseline to compare against.
        switch (BoolTransitionDetector.Detect(hadPrevious, previous, current))
        {
            case StateTransition.TurnedOn:
                alerts.Add((NotificationCategories.EngineStart, strings["EngineStartTitle"], strings["EngineStartBody"]));
                return false;

            case StateTransition.TurnedOff:
                _lastParkedAt[vin] = DateTime.UtcNow;
                return true;

            default:
                return false;
        }
    }

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
