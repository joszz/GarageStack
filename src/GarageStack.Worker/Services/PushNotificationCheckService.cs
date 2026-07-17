using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GarageStack.Worker.Services;

public class PushNotificationCheckService(
    ILogger<PushNotificationCheckService> logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender,
    TyrePressureThresholds tyrePressureThresholds) : BackgroundService
{
    private readonly NotificationCooldownGate _cooldownGate = new(TimeSpan.FromHours(1));
    internal readonly VinStateTracker<bool?> _engineRunningTracker = new();
    internal readonly VinStateTracker<bool?> _isChargingTracker = new();
    internal readonly Dictionary<string, DateTime> _lastParkedAt = new();
    private readonly TimeSpan _parkingGrace = TimeSpan.FromMinutes(10);

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
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vehicles = await db.Vehicles.ToListAsync(ct);

        foreach (var vehicle in vehicles)
        {
            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            if (snapshot is null) continue;

            // Seed in-memory parking time from DB on first sight of this VIN after a restart
            if (!_lastParkedAt.ContainsKey(vehicle.Vin) && vehicle.LastParkedAt.HasValue)
                _lastParkedAt[vehicle.Vin] = vehicle.LastParkedAt.Value;

            var vehicleType = GetVehicleType(vehicle);
            var alerts = new List<(string key, string title, string body)>();
            CheckTyrePressure(snapshot, alerts);
            CheckEvSoc(snapshot, vehicleType, alerts);
            CheckChargingComplete(snapshot, vehicle.Vin, vehicleType, alerts);
            var justParked = CheckEngineStart(snapshot, vehicle.Vin, alerts);
            if (justParked)
            {
                vehicle.LastParkedAt = _lastParkedAt[vehicle.Vin];
                await db.SaveChangesAsync(ct);
            }

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
                logger.LogInformation("Push sent: {Vin}/{Key} - {Title}", vehicle.Vin, key, title);
            }
        }
    }

    internal void CheckTyrePressure(Core.Models.TelemetrySnapshot s, List<(string, string, string)> alerts)
    {
        var low = new List<string>();
        if (s.TyrePressureFrontLeft is not null && s.TyrePressureFrontLeft < tyrePressureThresholds.LowBar) low.Add("FL");
        if (s.TyrePressureFrontRight is not null && s.TyrePressureFrontRight < tyrePressureThresholds.LowBar) low.Add("FR");
        if (s.TyrePressureRearLeft is not null && s.TyrePressureRearLeft < tyrePressureThresholds.LowBar) low.Add("RL");
        if (s.TyrePressureRearRight is not null && s.TyrePressureRearRight < tyrePressureThresholds.LowBar) low.Add("RR");

        if (low.Count > 0)
            alerts.Add(("low-tyre", "Low Tyre Pressure", $"Tyre pressure low: {string.Join(", ", low)}"));

        var high = new List<string>();
        if (s.TyrePressureFrontLeft is not null && s.TyrePressureFrontLeft > tyrePressureThresholds.HighBar) high.Add("FL");
        if (s.TyrePressureFrontRight is not null && s.TyrePressureFrontRight > tyrePressureThresholds.HighBar) high.Add("FR");
        if (s.TyrePressureRearLeft is not null && s.TyrePressureRearLeft > tyrePressureThresholds.HighBar) high.Add("RL");
        if (s.TyrePressureRearRight is not null && s.TyrePressureRearRight > tyrePressureThresholds.HighBar) high.Add("RR");

        if (high.Count > 0)
            alerts.Add(("high-tyre", "High Tyre Pressure", $"Tyre pressure high: {string.Join(", ", high)}"));
    }

    private static void CheckEvSoc(Core.Models.TelemetrySnapshot s, string vehicleType, List<(string, string, string)> alerts)
    {
        if (!CanCharge(vehicleType)) return;
        if (s.EvSocPercent is not null && s.EvSocPercent < 20)
            alerts.Add(("low-ev", "Low EV Battery", $"EV battery at {s.EvSocPercent:F0}%"));
    }

    internal void CheckChargingComplete(Core.Models.TelemetrySnapshot s, string vin, string vehicleType, List<(string, string, string)> alerts)
    {
        if (!CanCharge(vehicleType)) return;
        if (s.IsCharging is null) return;

        var current = s.IsCharging.Value;
        var hadPrevious = _isChargingTracker.TryUpdate(vin, current, out var previous);

        if (BoolTransitionDetector.Detect(hadPrevious, previous, current) != StateTransition.TurnedOff)
            return;

        // Charging finished while cable is still connected (session complete, not unplugged mid-charge)
        if (s.ChargerConnected == true)
        {
            var soc = s.EvSocPercent is not null ? $" (SOC: {s.EvSocPercent:F0}%)" : string.Empty;
            alerts.Add(("charging-complete", "Charging Complete", $"Your car has finished charging{soc}"));
        }
    }

    private static string GetVehicleType(Core.Models.Vehicle v) => VehicleTypeHelper.GetVehicleType(v);

    private static bool CanCharge(string vehicleType) => VehicleTypeHelper.CanCharge(vehicleType);

    internal bool CheckEngineStart(Core.Models.TelemetrySnapshot s, string vin, List<(string, string, string)> alerts)
    {
        if (s.EngineRunning is null) return false;

        var current = s.EngineRunning.Value;
        var hadPrevious = _engineRunningTracker.TryUpdate(vin, current, out var previous);

        // First observation after startup is skipped: no baseline to compare against.
        switch (BoolTransitionDetector.Detect(hadPrevious, previous, current))
        {
            case StateTransition.TurnedOn:
                alerts.Add(("engine-start", "Car Started", "Your car engine has started"));
                return false;

            case StateTransition.TurnedOff:
                _lastParkedAt[vin] = DateTime.UtcNow;
                return true;

            default:
                return false;
        }
    }

    private static bool IsParked(Core.Models.TelemetrySnapshot s)
        => s.EngineRunning == false;

    internal static void CheckUnlockedWhileParked(Core.Models.TelemetrySnapshot s, List<(string, string, string)> alerts, bool withinParkingGrace)
    {
        if (!IsParked(s) || withinParkingGrace) return;
        if (s.IsLocked is false)
            alerts.Add(("unlocked-parked", "Car Left Unlocked", "Your car is parked and unlocked"));
    }

    internal static void CheckDoorsOpenWhileParked(Core.Models.TelemetrySnapshot s, List<(string, string, string)> alerts, bool withinParkingGrace)
    {
        if (!IsParked(s) || withinParkingGrace) return;

        var open = new List<string>();
        if (s.DriverDoorOpen == true) open.Add("driver");
        if (s.PassengerDoorOpen == true) open.Add("passenger");
        if (s.RearLeftDoorOpen == true) open.Add("rear left");
        if (s.RearRightDoorOpen == true) open.Add("rear right");
        if (s.TrunkOpen == true) open.Add("boot");
        if (s.BonnetOpen == true) open.Add("bonnet");

        if (open.Count > 0)
            alerts.Add(("doors-open-parked", "Door Left Open", $"Door(s) open while parked: {string.Join(", ", open)}"));
    }

    internal static void CheckWindowsOpenWhileParked(Core.Models.TelemetrySnapshot s, List<(string, string, string)> alerts, bool withinParkingGrace)
    {
        if (!IsParked(s) || withinParkingGrace) return;

        var open = new List<string>();
        if (s.DriverWindowOpen == true) open.Add("driver");
        if (s.PassengerWindowOpen == true) open.Add("passenger");
        if (s.RearLeftWindowOpen == true) open.Add("rear left");
        if (s.RearRightWindowOpen == true) open.Add("rear right");

        if (open.Count > 0)
            alerts.Add(("windows-open-parked", "Window Left Open", $"Window(s) open while parked: {string.Join(", ", open)}"));
    }
}
