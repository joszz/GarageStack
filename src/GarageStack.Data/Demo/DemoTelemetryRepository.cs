using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

/// <summary>
/// Each demo car's telemetry: its generated history, and a live reading the demo controls and the
/// car's commands change. Every car keeps its own, so a change to one leaves the other as it was.
/// </summary>
public sealed class DemoTelemetryRepository : ITelemetryRepository
{
    private static long _nextId = 9000;
    private readonly Lock _lock = new();

    // The live reading of each car, by vehicle id, made when it is first asked for.
    private readonly Dictionary<int, TelemetrySnapshot> _current = [];

    // The electric range is derived from the state of charge, so the two stay in step when the demo
    // panel moves the state of charge.
    private static double ElectricRangeAt(DemoCar car, double socPercent) =>
        Math.Round(socPercent / 100.0 * car.FullBatteryRangeKm);

    // The live reading of a car. Call with the lock held.
    private TelemetrySnapshot Current(int vehicleId)
    {
        var car = DemoCars.For(vehicleId);
        if (!_current.TryGetValue(car.Vehicle.Id, out var current))
            _current[car.Vehicle.Id] = current = BuildDefaultSnapshot(car);
        return current;
    }

    public Task<long> AddAsync(TelemetrySnapshot snapshot, CancellationToken ct = default) =>
        Task.FromResult(Interlocked.Increment(ref _nextId));

    public Task MergeIntoAsync(long rowId, TelemetrySnapshot patch, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult<TelemetrySnapshot?>(Current(vehicleId).Clone()); }
    }

    public Task<IReadOnlyList<DailyStat>> GetDailyStatsAsync(
        int vehicleId, DateOnly from, DateOnly to, TimeZoneInfo zone, DateOnly today, CancellationToken ct = default)
    {
        var builder = new DailyStatsBuilder(zone, from, to, today);
        foreach (var snapshot in DemoCars.For(vehicleId).History.Snapshots.OrderBy(s => s.RecordedAt))
            builder.Add(TelemetryHistoryPoint.FromSnapshot(snapshot));
        return Task.FromResult(builder.Build());
    }

    // DemoTripRepository serves the demo's trips from each car's DemoTrips directly, so nothing asks the demo
    // telemetry for the fixes to cut them from.
    public Task<IReadOnlyList<TripPoint>> GetGpsFixesAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TripPoint>>([]);

    public Task<DateTime?> GetFirstGpsFixAtAsync(int vehicleId, CancellationToken ct = default) =>
        Task.FromResult<DateTime?>(null);

    // Only the Worker's recorder anchors trips, and demo mode runs no Worker.
    public Task<TripPoint?> GetLastGpsFixBeforeAsync(int vehicleId, DateTime before, CancellationToken ct = default) =>
        Task.FromResult<TripPoint?>(null);

    public Task<IReadOnlyList<DateTime>> GetReportTimesAsync(
        int vehicleId, DateTime after, DateTime before, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DateTime>>([]);

    // Maintenance asks for the reading now, which is the live snapshot's. Past readings are only
    // asked for by the Worker's recorder, and demo mode runs no Worker: DemoTrips carries those.
    public Task<double?> GetOdometerAtAsync(int vehicleId, DateTime at, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Current(vehicleId).OdometerKm); }
    }

    // Only the Worker's 12V alert asks, and demo mode runs no Worker.
    public Task<double?> GetBatteryVoltageAtAsync(int vehicleId, DateTime at, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Current(vehicleId).BatteryVoltage); }
    }

    // Only the Worker's recorder reads a trip's counter, and demo mode runs no Worker: DemoTrips
    // carries what each trip used.
    public Task<IReadOnlyList<double>> GetDayCounterReadingsAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<double>>([]);

    // The demo trips' own fixes, at a height made up from where they are: a gentle rise and fall
    // of a few metres, as a drive in the Netherlands has, so the trip log shows a profile.
    public Task<IReadOnlyList<ElevationReading>> GetElevationsAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ElevationReading>>(
        [
            .. DemoCars.For(vehicleId).Trips.All
                .SelectMany(t => t.Points)
                .Where(p => p.Source is null && p.RecordedAt >= from && p.RecordedAt <= to)
                .OrderBy(p => p.RecordedAt)
                .Select(p => new ElevationReading(p.RecordedAt, DemoElevation(p.Latitude, p.Longitude))),
        ]);

    private static double DemoElevation(double latitude, double longitude) =>
        Math.Round(4 + 3 * Math.Sin(latitude * 120) + 2 * Math.Cos(longitude * 90), 1);

    // Only the Worker's parked alerts weigh past polls, and demo mode runs no Worker.
    public Task<IReadOnlyList<StatusReading>> GetStatusReadingsAsync(int vehicleId, int count, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StatusReading>>([]);

    public Task<string?> GetChargingStopReasonAsync(int vehicleId, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public async Task<IReadOnlyList<ChargeSession>> GetChargeSessionsAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var readings = await ChargeReadingsAsync(
            vehicleId, from - ChargeSessionHistory.SessionLookaround, to + ChargeSessionHistory.SessionLookaround, ct);
        return ChargeSessionHistory.Build(readings, from, to);
    }

    public async Task<IReadOnlyList<ChargeSessionPowerPoint>> GetChargePowerAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        ChargeSessionHistory.PowerCurve(await ChargeReadingsAsync(vehicleId, from, to, ct));

    // The generated history, then the live snapshot the demo controls change.
    private async Task<List<ChargeSessionReading>> ChargeReadingsAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct)
    {
        var readings = DemoCars.For(vehicleId).History.Snapshots
            .Where(s => s.RecordedAt >= from && s.RecordedAt <= to)
            .Select(ChargeSessionReading.FromSnapshot)
            .ToList();
        if (await GetMergedLatestAsync(vehicleId, ct) is { } current && current.RecordedAt >= from && current.RecordedAt <= to)
            readings.Add(ChargeSessionReading.FromSnapshot(current));
        return readings;
    }

    // The generated history, oldest first, then the live snapshot the demo controls change.
    public async IAsyncEnumerable<TelemetrySnapshot> StreamAllAsync(
        int vehicleId, DateTime? since = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var snapshot in DemoCars.For(vehicleId).History.Snapshots.Where(s => since is null || s.RecordedAt >= since).OrderBy(s => s.RecordedAt))
        {
            ct.ThrowIfCancellationRequested();
            yield return snapshot;
        }
        var latest = (await GetMergedLatestAsync(vehicleId, ct))!;
        if (since is null || latest.RecordedAt >= since)
            yield return latest;
    }

    public Task<PositionFix?> GetLatestPositionAsync(int vehicleId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var current = Current(vehicleId);
            return Task.FromResult(current is { Latitude: { } lat, Longitude: { } lon }
                ? new PositionFix(lat, lon, current.Heading)
                : null);
        }
    }

    // Demo data is not ingested from MQTT, so there are no raw topics to report.

    public Task<VehicleAggregateStats> GetAggregateStatsAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var history = DemoCars.For(vehicleId).History.Snapshots.Where(s => s.RecordedAt >= from && s.RecordedAt <= to).ToList();
        var known = history.Count(s => s.ClimateOn != null);
        var on = history.Count(s => s.ClimateOn == true);
        var pct = known > 0 ? (int?)Math.Round((double)on / known * 100) : null;
        return Task.FromResult(new VehicleAggregateStats(pct, on, known));
    }

    /// <summary>Sets what the demo controls change on one car's live reading; the other cars keep theirs.</summary>
    public void ApplyOverride(int vehicleId, DemoStatusOverrideDto dto)
    {
        lock (_lock)
        {
            var car = DemoCars.For(vehicleId);
            var current = Current(vehicleId);
            // First, so a speed sent with it is the one that stays.
            if (dto.Driving.HasValue) SetDriving(car, current, dto.Driving.Value);
            if (dto.IsLocked.HasValue) current.IsLocked = dto.IsLocked;
            if (dto.EngineRunning.HasValue) current.EngineRunning = dto.EngineRunning;
            if (dto.ClimateOn.HasValue)
            {
                current.ClimateOn = dto.ClimateOn;
                current.ClimateMode = dto.ClimateOn.Value ? "on" : "off";
            }
            if (dto.DriverDoorOpen.HasValue) current.DriverDoorOpen = dto.DriverDoorOpen;
            if (dto.PassengerDoorOpen.HasValue) current.PassengerDoorOpen = dto.PassengerDoorOpen;
            if (dto.RearLeftDoorOpen.HasValue) current.RearLeftDoorOpen = dto.RearLeftDoorOpen;
            if (dto.RearRightDoorOpen.HasValue) current.RearRightDoorOpen = dto.RearRightDoorOpen;
            if (dto.TrunkOpen.HasValue) current.TrunkOpen = dto.TrunkOpen;
            if (dto.BonnetOpen.HasValue) current.BonnetOpen = dto.BonnetOpen;
            if (dto.DriverWindowOpen.HasValue) current.DriverWindowOpen = dto.DriverWindowOpen;
            if (dto.PassengerWindowOpen.HasValue) current.PassengerWindowOpen = dto.PassengerWindowOpen;
            if (dto.RearLeftWindowOpen.HasValue) current.RearLeftWindowOpen = dto.RearLeftWindowOpen;
            if (dto.RearRightWindowOpen.HasValue) current.RearRightWindowOpen = dto.RearRightWindowOpen;
            if (dto.ChargerConnected.HasValue || dto.IsCharging.HasValue || dto.ChargingType is not null || dto.ChargingPowerKw.HasValue)
                ApplyCharging(car, current, dto);
            if (dto.LightsMainBeam.HasValue) current.LightsMainBeam = dto.LightsMainBeam;
            if (dto.LightsDippedBeam.HasValue) current.LightsDippedBeam = dto.LightsDippedBeam;
            if (dto.LightsSide.HasValue) current.LightsSide = dto.LightsSide;
            if (dto.EvSocPercent.HasValue)
            {
                current.EvSocPercent = dto.EvSocPercent;
                current.HvSocKwh = Math.Round(dto.EvSocPercent.Value / 100.0 * car.CapacityKwh, 1);
                current.ElectricRangeKm = ElectricRangeAt(car, dto.EvSocPercent.Value);
            }
            if (dto.InteriorTemperature.HasValue) current.InteriorTemperature = dto.InteriorTemperature;
            if (dto.ExteriorTemperature.HasValue) current.ExteriorTemperature = dto.ExteriorTemperature;
            if (dto.Speed.HasValue) current.Speed = dto.Speed;
            if (dto.IsAvailable.HasValue) current.IsAvailable = dto.IsAvailable;
            if (dto.FuelLevelPercent is { } fuel)
            {
                current.FuelLevelPercent = fuel;
                current.FuelRangeKm = Math.Round(fuel / 100.0 * car.FuelRangeKm);
            }
            if (dto.TyrePressureFrontLeft.HasValue) current.TyrePressureFrontLeft = dto.TyrePressureFrontLeft;
            if (dto.TyrePressureFrontRight.HasValue) current.TyrePressureFrontRight = dto.TyrePressureFrontRight;
            if (dto.TyrePressureRearLeft.HasValue) current.TyrePressureRearLeft = dto.TyrePressureRearLeft;
            if (dto.TyrePressureRearRight.HasValue) current.TyrePressureRearRight = dto.TyrePressureRearRight;
            if (dto.BatteryVoltage.HasValue) current.BatteryVoltage = dto.BatteryVoltage;
        }
    }

    // On the road: engine on, unplugged, moving, and the trip in progress counted when the car has
    // one. Parked: engine off and standing still, with no journey under way.
    private static void SetDriving(DemoCar car, TelemetrySnapshot current, bool driving)
    {
        current.EngineRunning = driving;
        current.HvBatteryActive = driving;
        current.Speed = driving ? current.Speed is > 0 ? current.Speed : 50 : 0;
        current.CurrentJourneyDistance = driving ? Math.Round(car.Trips.InProgressDistanceKm, 1) : 0;
        if (!driving) return;
        current.ChargerConnected = false;
        current.IsCharging = false;
        SetChargePower(current, null);
    }

    // The cable and the charge as the car reports them together: a charge needs the cable in, and
    // pulling the cable ends it. A charge shows its power: the one sent, the one it had, or what a
    // charger of its kind typically gives.
    private static void ApplyCharging(DemoCar car, TelemetrySnapshot current, DemoStatusOverrideDto dto)
    {
        if (dto.ChargingType?.ToUpperInvariant() is "AC" or "DC")
            current.ChargingType = dto.ChargingType.ToUpperInvariant();
        if (dto.ChargerConnected.HasValue)
        {
            current.ChargerConnected = dto.ChargerConnected;
            if (dto.ChargerConnected == false) current.IsCharging = false;
        }
        if (dto.IsCharging.HasValue)
        {
            current.IsCharging = dto.IsCharging;
            if (dto.IsCharging == true) current.ChargerConnected = true;
        }

        var dc = current.ChargingType == "DC";
        var power = dto.ChargingPowerKw is > 0 ? dto.ChargingPowerKw
            : ChargePower(current) is > 0 and var had && dto.ChargingType is null ? had
            : dc ? 75 : car.HomeChargerKw;
        SetChargePower(current, current.IsCharging == true ? power : null);
        current.BmsChargeStatus = current.IsCharging == true ? "CHARGING_1"
            : current.ChargerConnected == true ? "CHARGING_STOPPED"
            : "UNPLUGGED";
        current.ChargingCableLock = current.ChargerConnected == true;
    }

    // What the charger gives now, as SetChargePower wrote it: the onboard charger's draw on AC, the
    // battery's intake on DC.
    private static double? ChargePower(TelemetrySnapshot current) =>
        current.IsCharging != true ? null
        : current.ChargingType == "DC" ? -current.HvPower
        : current.ObcPowerSinglePhase;

    // The battery takes what the charger gives less its losses; DC bypasses the onboard charger.
    // Null stops the charge's power figures.
    private static void SetChargePower(TelemetrySnapshot current, double? powerKw)
    {
        var dc = current.ChargingType == "DC";
        current.ObcPowerSinglePhase = powerKw is { } ac && !dc ? Math.Round(ac, 1) : 0;
        current.HvPower = powerKw is { } kw ? -Math.Round(kw * (dc ? 1 : 0.9), 1) : current.EngineRunning == true ? 16.2 : 0;
    }

    /// <summary>
    /// Carries out a command the way the car would, on one demo car's state: the command name and
    /// value as <see cref="VehicleCommands"/> checked them. Returns why the car refused, the way the
    /// gateway reports it, or null when it carried the command out.
    /// </summary>
    public string? ApplyCommand(int vehicleId, string command, string value)
    {
        lock (_lock)
        {
            var current = Current(vehicleId);
            var refusal = Apply(DemoCars.For(vehicleId), current, command, value);
            if (refusal is null)
            {
                current.RecordedAt = DateTime.UtcNow;
                current.LastVehicleStateAt = current.RecordedAt;
            }
            return refusal;
        }
    }

    // The car's side of each command. Horn and lights and a refresh change nothing the dashboard
    // shows beyond the reading's time. Unlatching the boot pops it open; a power tailgate moves
    // either way.
    private static string? Apply(DemoCar demoCar, TelemetrySnapshot car, string command, string value)
    {
        switch (command)
        {
            case "climate":
                car.ClimateOn = value != "off";
                car.ClimateMode = value;
                break;
            case "climate-temperature":
                car.RemoteTemperature = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "rear-defroster":
                car.RearWindowDefroster = value == "on";
                break;
            case "seat-left":
                car.HeatedSeatFrontLeft = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "seat-right":
                car.HeatedSeatFrontRight = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "steering-wheel-heating":
                car.SteeringWheelHeating = value == "true";
                break;
            case "lock":
                car.IsLocked = value == "True";
                break;
            case "boot":
                car.TrunkOpen = true;
                break;
            case "power-tailgate":
                car.TrunkOpen = value == "open";
                break;
            case "charge-limit":
                car.ChargeCurrentLimit = value;
                break;
            case "charge-target":
                car.SocTargetPercent = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "charging" when value == "true" && car.ChargerConnected != true:
                return "Failed: the charging cable is not plugged in";
            case "charging":
                car.IsCharging = value == "true";
                car.BmsChargeStatus = value == "true" ? "CHARGING_1" : "CHARGING_STOPPED";
                SetChargePower(car, value == "true" ? car.ChargingType == "DC" ? 75 : demoCar.HomeChargerKw : null);
                break;
            case "charging-cable-lock":
                car.ChargingCableLock = value == "true";
                break;
            case "battery-heating":
                car.BatteryHeating = value == "true";
                break;
            case "scheduled-charging" when SafeJson.TryDeserialize<Dictionary<string, string>>(value) is { } schedule:
                car.ChargingScheduleMode = schedule["mode"];
                car.ChargingScheduleStartTime = schedule["startTime"];
                car.ChargingScheduleEndTime = schedule["endTime"];
                break;
            case "battery-heating-schedule" when SafeJson.TryDeserialize<Dictionary<string, string>>(value) is { } schedule:
                car.BatteryHeatingScheduleMode = schedule["mode"];
                car.BatteryHeatingScheduleStartTime = schedule["startTime"];
                break;
        }
        return null;
    }

    // The car's live reading as the demo starts. A car on the road carries on from its history with
    // the trip it is driving; a parked one stands where its last trip ended, plugged in at home and
    // waiting for its charging schedule.
    private static TelemetrySnapshot BuildDefaultSnapshot(DemoCar car)
    {
        var trips = car.Trips;
        var position = trips.Position;
        var driving = trips.InProgress is not null;

        var history = car.History.Last;
        var drivingKm = trips.InProgressDistanceKm;
        var drivingKwh = drivingKm * car.InProgressKwhPerKm;
        var soc = Math.Round(history.SocPercent - drivingKwh / car.CapacityKwh * 100);

        return new()
        {
            Id = 1,
            VehicleId = car.Vehicle.Id,
            RecordedAt = DateTime.UtcNow,
            FuelLevelPercent = history.FuelPercent,
            FuelRangeKm = Math.Round(history.FuelPercent / 100.0 * car.FuelRangeKm),
            EvSocPercent = soc,
            ElectricRangeKm = ElectricRangeAt(car, soc),
            HvSocKwh = Math.Round(soc / 100.0 * car.CapacityKwh, 1),
            HvTotalCapacityKwh = car.CapacityKwh,
            HvVoltage = car.NominalVoltage,
            HvCurrent = driving ? 42.0 : 0.0,
            HvPower = driving ? 16.2 : 0.0,
            HvBatteryActive = driving,
            OdometerKm = car.CurrentOdometerKm,
            EngineRunning = driving,
            IsCharging = false,
            ChargerConnected = !driving,
            ChargingType = "AC",
            ChargingCableLock = !driving,
            BmsChargeStatus = driving ? "UNPLUGGED" : "CHARGING_STOPPED",
            SocTargetPercent = VehicleTypeHelper.SupportsTargetSoc(car.Vehicle) ? (int)car.ChargeTargetPercent : null,
            ChargeCurrentLimit = "Max",
            OnboardChargerPlugStatus = 0,
            OffboardChargerPlugStatus = 0,
            ObcVoltage = 0.0,
            ObcCurrent = 0.0,
            ObcPowerSinglePhase = 0.0,
            InteriorTemperature = driving ? 21.0 : 16.0,
            ExteriorTemperature = 14.0,
            RemoteTemperature = 19.5,
            Speed = driving ? position.Speed : 0,
            Heading = driving ? 150 : 270,
            CurrentJourneyDistance = Math.Round(drivingKm, 1),
            IsLocked = true,
            ClimateOn = false,
            ClimateMode = "off",
            BatteryHeating = false,
            DriverDoorOpen = false,
            PassengerDoorOpen = false,
            RearLeftDoorOpen = false,
            RearRightDoorOpen = false,
            TrunkOpen = false,
            BonnetOpen = false,
            DriverWindowOpen = false,
            PassengerWindowOpen = false,
            RearLeftWindowOpen = false,
            RearRightWindowOpen = false,
            SunRoofOpen = null,
            TyrePressureFrontLeft = car.FrontTyreBar,
            TyrePressureFrontRight = car.FrontTyreBar,
            TyrePressureRearLeft = car.RearTyreBar,
            // Where a slow leak in the history has brought it: still above the low line.
            TyrePressureRearRight = Math.Round(car.History.RearRightTyreBarNow, 2),
            Latitude = position.Latitude,
            Longitude = position.Longitude,
            Elevation = 3.0,
            BatteryVoltage = car.AuxBatteryVolts + 0.1,
            LightsMainBeam = false,
            LightsDippedBeam = false,
            LightsSide = false,
            HeatedSeatFrontLeft = 0,
            HeatedSeatFrontRight = 0,
            RearWindowDefroster = false,
            SteeringWheelHeating = false,
            IsAvailable = true,
            LastVehicleStateAt = DateTime.UtcNow,
            LastChargeStateAt = DateTime.UtcNow.AddHours(-8),
            MileageSinceLastCharge = Math.Round(history.SinceChargeKm + drivingKm, 1),
            MileageOfTheDay = Math.Round(history.DayKm + drivingKm, 1),
            PowerUsageOfDay = Math.Round(history.DayKwh + drivingKwh, 2),
            PowerUsageSinceLastCharge = Math.Round(history.SinceChargeKwh + drivingKwh, 2),
            ChargingScheduleMode = "UNTIL_CONFIGURED_TIME",
            ChargingScheduleStartTime = car.ChargingSchedule.Start,
            ChargingScheduleEndTime = car.ChargingSchedule.End,
        };
    }
}
