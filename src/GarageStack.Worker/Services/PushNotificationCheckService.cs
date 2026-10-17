using System.Globalization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Worker.Mqtt;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Services;

public class PushNotificationCheckService(
    ILogger<PushNotificationCheckService> logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender,
    TyrePressureThresholds tyrePressureThresholds,
    AlertThresholds alertThresholds,
    IStringLocalizer<NotificationStrings> strings) : PeriodicBackgroundService(logger)
{
    private readonly NotificationCooldownGate _cooldownGate = new(NotificationCooldownGate.AlertCooldown);
    internal readonly VinStateTracker<bool?> _isChargingTracker = new();
    private readonly TimeSpan _parkingGrace = TimeSpan.FromMinutes(10);

    // Enough polls to span ParkedProblem.MinimumDuration even from a drive, when the gateway polls
    // every forty seconds or so: a window left open counts from before the car parked.
    private const int StatusReadingsToWeigh = 20;

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
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var vehicles = await vehicleRepo.GetAllAsync(ct);

        foreach (var vehicle in vehicles)
        {
            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            if (snapshot is null) continue;

            var now = DateTime.UtcNow;
            await CheckLevelsAsync(vehicle, snapshot, telemetry, notifications, now, ct);
            await CheckParkedAsync(vehicle, snapshot, telemetry, notifications, now, ct);
        }
    }

    /// <summary>
    /// Warns about tyres, fuel and charge out of bounds, a charge that stopped, and a car gone
    /// quiet. Each warning goes out once per report: a car asleep overnight would otherwise be
    /// reported every hour on the one reading it last sent.
    /// </summary>
    internal async Task CheckLevelsAsync(
        Vehicle vehicle, TelemetrySnapshot snapshot, ITelemetryRepository telemetry, INotificationRepository notifications, DateTime now, CancellationToken ct)
    {
        var vehicleType = VehicleTypeHelper.GetVehicleType(vehicle);
        var stopReason = VehicleTypeHelper.CanCharge(vehicleType)
            ? await telemetry.GetChargingStopReasonAsync(vehicle.Id, ct)
            : null;
        var alerts = new List<(string key, string title, string body)>();
        CheckTyrePressure(snapshot, alerts);
        CheckEvSoc(snapshot, vehicleType, alerts);
        CheckFuelLevel(snapshot, vehicleType, alerts);
        CheckChargingStopped(snapshot, vehicle.Vin, vehicleType, stopReason, alerts);
        CheckDataStale(snapshot, now, alerts);
        if (alerts.Count == 0) return;

        await SendAsync(vehicle, alerts, notifications, ct, onceSince: NewestReportAt(snapshot));
    }

    /// <summary>
    /// When the car last reported, by the gateway's stamps on its polls. A message the broker
    /// replays on reconnect carries its old stamp, so it does not count as a new report the way
    /// the time its row was written would.
    /// </summary>
    internal static DateTime NewestReportAt(TelemetrySnapshot s) =>
        (s.LastVehicleStateAt, s.LastChargeStateAt) switch
        {
            ({ } vehicleState, { } chargeState) => vehicleState > chargeState ? vehicleState : chargeState,
            ({ } vehicleState, null) => vehicleState,
            (null, { } chargeState) => chargeState,
            _ => s.RecordedAt,
        };

    /// <summary>
    /// Warns about a car parked unlocked, with a door or window open, or with a low 12V battery,
    /// once its polls have shown it for a while. Every warning rests on the newest poll and goes
    /// out once for it: a car asleep overnight would otherwise be reported every hour on the one
    /// poll it last sent. A car that left the spot it parked on is warned about once per parking.
    /// </summary>
    internal async Task CheckParkedAsync(
        Vehicle vehicle, TelemetrySnapshot snapshot, ITelemetryRepository telemetry, INotificationRepository notifications, DateTime now, CancellationToken ct)
    {
        // The MQTT consumer records when the engine last stopped (it also sends the
        // engine-start push, the moment the car reports it). A car parked a moment ago is
        // being unloaded, not left open, so the parked alerts wait out a grace period.
        if (!IsParked(snapshot) || IsWithinParkingGrace(vehicle.LastParkedAt, now)) return;

        var readings = await telemetry.GetStatusReadingsAsync(vehicle.Id, StatusReadingsToWeigh, ct);
        var alerts = new List<(string key, string title, string body)>();
        CheckUnlockedWhileParked(readings, alerts);
        CheckDoorsOpenWhileParked(readings, alerts);
        CheckWindowsOpenWhileParked(readings, alerts);
        CheckAuxBatteryWhileParked(readings, alerts);
        if (alerts.Count > 0)
            await SendAsync(vehicle, alerts, notifications, ct, onceSince: readings[0].ArrivedAt);

        var moved = new List<(string key, string title, string body)>();
        CheckMovedWhileParked(vehicle, snapshot, moved);
        if (moved.Count > 0)
            await SendAsync(vehicle, moved, notifications, ct, onceSince: vehicle.LastParkedAt);
    }

    // Further than GPS wanders in a car park or a garage, nearer than a tow truck takes a car.
    internal const double MovedWhileParkedMeters = 300;

    /// <summary>
    /// Warns when the car reports a position well away from where it parked while the engine stayed
    /// off: towed, lifted onto a truck, or taken on a ferry. A car only says where it is when it
    /// reports, so the warning comes with its first report after the move.
    /// </summary>
    internal void CheckMovedWhileParked(Vehicle vehicle, TelemetrySnapshot snapshot, List<(string, string, string)> alerts)
    {
        if (vehicle is not { ParkedLatitude: { } parkedLat, ParkedLongitude: { } parkedLon }) return;
        if (snapshot is not { Latitude: { } lat, Longitude: { } lon }) return;

        var meters = GeoHelper.Haversine(parkedLat, parkedLon, lat, lon) * 1000;
        if (meters < MovedWhileParkedMeters) return;

        // In the notification's language, as its words are: 1,2 km in Dutch.
        var distance = (meters / 1000).ToString("F1", CultureInfo.CurrentUICulture);
        alerts.Add((NotificationCategories.MovedParked, strings["MovedParkedTitle"], strings["MovedParkedBody", distance]));
    }

    // With onceSince, an alert is held back when one of its kind already went out after that moment.
    private async Task SendAsync(
        Vehicle vehicle, List<(string key, string title, string body)> alerts, INotificationRepository notifications, CancellationToken ct, DateTime? onceSince = null)
    {
        foreach (var (key, title, body) in alerts)
        {
            if (onceSince is { } since && await notifications.WasSentSinceAsync(key, vehicle.Id, since, ct))
                continue;

            // VehicleId is included in the DB check so one vehicle's alert cannot suppress
            // another vehicle's same-category alert.
            var shouldNotify = await _cooldownGate.ShouldNotifyAsync(vehicle.Vin, vehicle.Id, key, notifications, ct);
            if (!shouldNotify) continue;

            await pushSender.SendToAllAsync(title, body, ct, key, vehicle.Id);
            logger.LogInformation("Push sent: {Vin}/{Key} - {Title}", LogRedaction.Vin(vehicle.Vin), key, title);
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

    internal void CheckEvSoc(TelemetrySnapshot s, string vehicleType, List<(string, string, string)> alerts)
    {
        if (!VehicleTypeHelper.CanCharge(vehicleType)) return;
        if (s.EvSocPercent is { } soc && soc < alertThresholds.LowEvPercent)
            alerts.Add((NotificationCategories.LowEv, strings["LowEvTitle"], strings["LowEvBody", $"{soc:F0}"]));
    }

    // A car without a combustion engine can still report a fuel level of zero.
    internal void CheckFuelLevel(TelemetrySnapshot s, string vehicleType, List<(string, string, string)> alerts)
    {
        if (!VehicleTypeHelper.HasCombustionEngine(vehicleType)) return;
        if (s.FuelLevelPercent is { } fuel && fuel < alertThresholds.LowFuelPercent)
            alerts.Add((NotificationCategories.LowFuel, strings["LowFuelTitle"], strings["LowFuelBody", $"{fuel:F0}"]));
    }

    /// <summary>
    /// Warns when the car has gone quiet for longer than the gateway's daily poll of a sleeping
    /// car explains: the MG session has lapsed, the gateway is down, or the car is out of reach.
    /// Fresh data moves the newest report on and re-arms it.
    /// </summary>
    internal void CheckDataStale(TelemetrySnapshot s, DateTime now, List<(string, string, string)> alerts)
    {
        if (alertThresholds.StaleDataHours <= 0) return;

        var silentFor = now - NewestReportAt(s);
        if (silentFor.TotalHours < alertThresholds.StaleDataHours) return;

        alerts.Add((NotificationCategories.DataStale, strings["DataStaleTitle"], strings["DataStaleBody", $"{silentFor.TotalHours:F0}"]));
    }

    /// <summary>
    /// Tells a finished charge from one the car broke off. A fault the car names is reported
    /// whatever the cable says, since a plug not fully in can read as no cable at all; without one,
    /// a stop with the cable still in is a finished charge, and one without it was unplugged by
    /// whoever is standing at the car.
    /// </summary>
    internal void CheckChargingStopped(
        TelemetrySnapshot s, string vin, string vehicleType, string? stopReason, List<(string, string, string)> alerts)
    {
        if (!VehicleTypeHelper.CanCharge(vehicleType)) return;
        if (s.IsCharging is null) return;

        var current = s.IsCharging.Value;
        var hadPrevious = _isChargingTracker.TryUpdate(vin, current, out var previous);

        if (BoolTransitionDetector.Detect(hadPrevious, previous, current) != StateTransition.TurnedOff)
            return;

        if (GatewayChargingStopReason.IsFault(stopReason))
        {
            var reason = strings[GatewayChargingStopReason.ResourceKey(stopReason!)].Value;
            var body = s.EvSocPercent is not null
                ? strings["ChargingInterruptedBodyWithSoc", reason, $"{s.EvSocPercent:F0}"]
                : strings["ChargingInterruptedBody", reason];
            alerts.Add((NotificationCategories.ChargingInterrupted, strings["ChargingInterruptedTitle"], body));
        }
        else if (s.ChargerConnected == true)
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

    internal void CheckUnlockedWhileParked(IReadOnlyList<StatusReading> readings, List<(string, string, string)> alerts)
    {
        if (ParkedProblem.Persists(readings, s => s.IsLocked is false))
            alerts.Add((NotificationCategories.UnlockedParked, strings["UnlockedParkedTitle"], strings["UnlockedParkedBody"]));
    }

    // While the car is on, the traction battery tops the 12V battery up, so only a parked car's
    // voltage says how the battery is doing. Several polls in a row keep one dip from counting.
    internal void CheckAuxBatteryWhileParked(IReadOnlyList<StatusReading> readings, List<(string, string, string)> alerts)
    {
        if (!ParkedProblem.Persists(readings, s => s.BatteryVoltage < alertThresholds.AuxBatteryLowVolt)) return;

        var voltage = readings[0].State.BatteryVoltage!.Value.ToString("0.0", CultureInfo.CurrentUICulture);
        alerts.Add((NotificationCategories.Low12V, strings["Low12VTitle"], strings["Low12VBody", voltage]));
    }

    internal void CheckDoorsOpenWhileParked(IReadOnlyList<StatusReading> readings, List<(string, string, string)> alerts) =>
        CheckOpenWhileParked(readings, DoorPositions, alerts, NotificationCategories.DoorsOpenParked, "DoorsOpenTitle", "DoorsOpenBody");

    internal void CheckWindowsOpenWhileParked(IReadOnlyList<StatusReading> readings, List<(string, string, string)> alerts) =>
        CheckOpenWhileParked(readings, WindowPositions, alerts, NotificationCategories.WindowsOpenParked, "WindowsOpenTitle", "WindowsOpenBody");

    // The body names what the newest poll shows open: a door shut since the first poll is not listed.
    private void CheckOpenWhileParked(
        IReadOnlyList<StatusReading> readings,
        (string ResourceKey, Func<TelemetrySnapshot, bool?> Read)[] positions,
        List<(string, string, string)> alerts,
        string category,
        string titleKey,
        string bodyKey)
    {
        if (!ParkedProblem.Persists(readings, s => positions.Any(p => p.Read(s) == true))) return;

        var open = OpenPositions(readings[0].State, positions);
        alerts.Add((category, strings[titleKey], strings[bodyKey, string.Join(", ", open)]));
    }

    private List<string> OpenPositions(TelemetrySnapshot s, (string ResourceKey, Func<TelemetrySnapshot, bool?> Read)[] positions) =>
        positions
            .Where(p => p.Read(s) == true)
            .Select(p => strings[p.ResourceKey].Value)
            .ToList();
}
