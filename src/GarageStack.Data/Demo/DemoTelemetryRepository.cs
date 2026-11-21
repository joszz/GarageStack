using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

public sealed class DemoTelemetryRepository : ITelemetryRepository
{
    private static long _nextId = 9000;
    private static readonly object _lock = new();
    private static IReadOnlyList<TelemetrySnapshot> History =>
        DemoTelemetryCapture.Snapshots ?? DemoHistory.Snapshots;

    private const double FullBatteryRangeKm = 400.0;

    private static double ElectricRangeAt(double socPercent) =>
        Math.Round(socPercent / 100.0 * FullBatteryRangeKm);

    private static double? ElectricRangeAt(TelemetrySnapshot snapshot, double socPercent) =>
        snapshot.ElectricRangeKm is { } range && snapshot.EvSocPercent is { } currentSoc && currentSoc > 0
            ? Math.Round(range / currentSoc * socPercent)
            : null;

    private TelemetrySnapshot _current = DemoTelemetryCapture.Snapshots is { Count: > 0 } captured
        ? DemoTelemetryCapture.MergeLatest(captured)
        : BuildDefaultSnapshot();

    public Task<long> AddAsync(TelemetrySnapshot snapshot, CancellationToken ct = default) =>
        Task.FromResult(Interlocked.Increment(ref _nextId));

    public Task MergeIntoAsync(long rowId, TelemetrySnapshot patch, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult<TelemetrySnapshot?>(_current.Clone()); }
    }

    public Task<IReadOnlyList<DailyStat>> GetDailyStatsAsync(
        int vehicleId, DateOnly from, DateOnly to, TimeZoneInfo zone, DateOnly today, CancellationToken ct = default)
    {
        var builder = new DailyStatsBuilder(zone, from, to, today);
        foreach (var snapshot in History.OrderBy(s => s.RecordedAt))
            builder.Add(TelemetryHistoryPoint.FromSnapshot(snapshot));
        return Task.FromResult(builder.Build());
    }

    public Task<IReadOnlyList<TripPoint>> GetGpsFixesAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (DemoTelemetryCapture.Snapshots is not { } captured)
            return Task.FromResult<IReadOnlyList<TripPoint>>([]);

        IReadOnlyList<TripPoint> fixes = captured
            .Where(s => s.RecordedAt >= from && s.RecordedAt < to && s.Latitude is not null && s.Longitude is not null)
            .OrderBy(s => s.RecordedAt)
            .Select(s => new TripPoint(s.RecordedAt, s.Latitude!.Value, s.Longitude!.Value, s.Speed, null))
            .ToArray();
        return Task.FromResult(fixes);
    }

    public Task<DateTime?> GetFirstGpsFixAtAsync(int vehicleId, CancellationToken ct = default)
    {
        if (DemoTelemetryCapture.Snapshots is not { } captured)
            return Task.FromResult<DateTime?>(null);

        return Task.FromResult(captured
            .Where(s => s.Latitude is not null && s.Longitude is not null)
            .Select(s => (DateTime?)s.RecordedAt)
            .Min());
    }

    public Task<TripPoint?> GetLastGpsFixBeforeAsync(int vehicleId, DateTime before, CancellationToken ct = default)
    {
        if (DemoTelemetryCapture.Snapshots is not { } captured)
            return Task.FromResult<TripPoint?>(null);

        return Task.FromResult(captured
            .Where(s => s.RecordedAt < before && s.Latitude is not null && s.Longitude is not null)
            .OrderByDescending(s => s.RecordedAt)
            .Select(s => new TripPoint(s.RecordedAt, s.Latitude!.Value, s.Longitude!.Value, s.Speed, null))
            .FirstOrDefault());
    }

    public Task<IReadOnlyList<DateTime>> GetReportTimesAsync(
        int vehicleId, DateTime after, DateTime before, int limit, CancellationToken ct = default)
    {
        if (DemoTelemetryCapture.Snapshots is not { } captured)
            return Task.FromResult<IReadOnlyList<DateTime>>([]);

        IReadOnlyList<DateTime> times = captured
            .Where(s => s.RecordedAt > after && s.RecordedAt < before)
            .OrderByDescending(s => s.RecordedAt)
            .Take(limit)
            .Select(s => s.RecordedAt)
            .ToArray();
        return Task.FromResult(times);
    }

    // Maintenance asks for the reading now, which is the live snapshot's. Past readings are only
    // asked for by the Worker's recorder, and demo mode runs no Worker.
    public Task<double?> GetOdometerAtAsync(int vehicleId, DateTime at, CancellationToken ct = default)
    {
        if (DemoTelemetryCapture.Snapshots is { } captured)
        {
            var odometer = captured
                .Where(snapshot => snapshot.RecordedAt <= at && snapshot.OdometerKm is not null)
                .OrderByDescending(snapshot => snapshot.RecordedAt)
                .Select(snapshot => snapshot.OdometerKm)
                .FirstOrDefault();
            return Task.FromResult(odometer);
        }
        lock (_lock) { return Task.FromResult(_current.OdometerKm); }
    }

    public Task<IReadOnlyList<StatusReading>> GetStatusReadingsAsync(int vehicleId, int count, CancellationToken ct = default)
    {
        if (DemoTelemetryCapture.Snapshots is not { } captured)
            return Task.FromResult<IReadOnlyList<StatusReading>>([]);

        IReadOnlyList<StatusReading> readings = captured
            .Where(snapshot => snapshot.IsLocked is not null || snapshot.DriverDoorOpen is not null ||
                snapshot.PassengerDoorOpen is not null || snapshot.RearLeftDoorOpen is not null ||
                snapshot.RearRightDoorOpen is not null || snapshot.TrunkOpen is not null ||
                snapshot.BonnetOpen is not null || snapshot.DriverWindowOpen is not null ||
                snapshot.PassengerWindowOpen is not null || snapshot.RearLeftWindowOpen is not null ||
                snapshot.RearRightWindowOpen is not null)
            .OrderByDescending(snapshot => snapshot.RecordedAt)
            .Take(count)
            .Select(snapshot => new StatusReading(snapshot.RecordedAt, snapshot))
            .ToArray();
        return Task.FromResult(readings);
    }

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

    private async Task<List<ChargeSessionReading>> ChargeReadingsAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct)
    {
        var history = History;
        var readings = history
            .Where(snapshot => snapshot.RecordedAt >= from && snapshot.RecordedAt <= to)
            .Select(ChargeSessionReading.FromSnapshot)
            .ToList();

        if (await GetMergedLatestAsync(vehicleId, ct) is { } current &&
            current.RecordedAt >= from && current.RecordedAt <= to &&
            history.Count > 0 && current.RecordedAt > history[^1].RecordedAt)
            readings.Add(ChargeSessionReading.FromSnapshot(current));
        return readings;
    }

    public async IAsyncEnumerable<TelemetrySnapshot> StreamAllAsync(
        int vehicleId, DateTime? since = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var latest = (await GetMergedLatestAsync(vehicleId, ct))!;
        foreach (var snapshot in History
            .Where(s => s.Id != latest.Id && (since is null || s.RecordedAt >= since))
            .OrderBy(s => s.RecordedAt))
        {
            ct.ThrowIfCancellationRequested();
            yield return snapshot;
        }
        if (since is null || latest.RecordedAt >= since)
            yield return latest;
    }

    public Task<PositionFix?> GetLatestPositionAsync(int vehicleId, CancellationToken ct = default)
    {
        if (DemoTelemetryCapture.Snapshots is { } captured)
        {
            var fix = captured
                .Where(snapshot => snapshot.Latitude is not null && snapshot.Longitude is not null)
                .OrderByDescending(snapshot => snapshot.RecordedAt)
                .FirstOrDefault();
            return Task.FromResult(fix is null
                ? null
                : new PositionFix(fix.Latitude!.Value, fix.Longitude!.Value, fix.Heading));
        }
        lock (_lock)
        {
            return Task.FromResult(_current is { Latitude: { } lat, Longitude: { } lon }
                ? new PositionFix(lat, lon, _current.Heading)
                : null);
        }
    }

    // Demo data is not ingested from MQTT, so there are no raw topics to report.

    public Task<VehicleAggregateStats> GetAggregateStatsAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var history = History.Where(s => s.RecordedAt >= from && s.RecordedAt <= to).ToList();
        var known = history.Count(s => s.ClimateOn != null);
        var on = history.Count(s => s.ClimateOn == true);
        var pct = known > 0 ? (int?)Math.Round((double)on / known * 100) : null;
        return Task.FromResult(new VehicleAggregateStats(pct, on, known));
    }

    public void ApplyOverride(DemoStatusOverrideDto dto)
    {
        lock (_lock)
        {
            if (dto.IsLocked.HasValue) _current.IsLocked = dto.IsLocked;
            if (dto.EngineRunning.HasValue) _current.EngineRunning = dto.EngineRunning;
            if (dto.ClimateOn.HasValue)
            {
                _current.ClimateOn = dto.ClimateOn;
                _current.ClimateMode = dto.ClimateOn.Value ? "on" : "off";
            }
            if (dto.DriverDoorOpen.HasValue) _current.DriverDoorOpen = dto.DriverDoorOpen;
            if (dto.PassengerDoorOpen.HasValue) _current.PassengerDoorOpen = dto.PassengerDoorOpen;
            if (dto.RearLeftDoorOpen.HasValue) _current.RearLeftDoorOpen = dto.RearLeftDoorOpen;
            if (dto.RearRightDoorOpen.HasValue) _current.RearRightDoorOpen = dto.RearRightDoorOpen;
            if (dto.TrunkOpen.HasValue) _current.TrunkOpen = dto.TrunkOpen;
            if (dto.BonnetOpen.HasValue) _current.BonnetOpen = dto.BonnetOpen;
            if (dto.DriverWindowOpen.HasValue) _current.DriverWindowOpen = dto.DriverWindowOpen;
            if (dto.PassengerWindowOpen.HasValue) _current.PassengerWindowOpen = dto.PassengerWindowOpen;
            if (dto.RearLeftWindowOpen.HasValue) _current.RearLeftWindowOpen = dto.RearLeftWindowOpen;
            if (dto.RearRightWindowOpen.HasValue) _current.RearRightWindowOpen = dto.RearRightWindowOpen;
            if (dto.ChargerConnected.HasValue) _current.ChargerConnected = dto.ChargerConnected;
            if (dto.IsCharging.HasValue) _current.IsCharging = dto.IsCharging;
            if (dto.LightsMainBeam.HasValue) _current.LightsMainBeam = dto.LightsMainBeam;
            if (dto.LightsDippedBeam.HasValue) _current.LightsDippedBeam = dto.LightsDippedBeam;
            if (dto.LightsSide.HasValue) _current.LightsSide = dto.LightsSide;
            if (dto.EvSocPercent.HasValue)
            {
                var electricRange = ElectricRangeAt(_current, dto.EvSocPercent.Value);
                _current.EvSocPercent = dto.EvSocPercent;
                if (_current.HvTotalCapacityKwh is { } capacity && capacity > 0)
                    _current.HvSocKwh = Math.Round(dto.EvSocPercent.Value / 100.0 * capacity, 1);
                if (electricRange is { } range) _current.ElectricRangeKm = range;
            }
            if (dto.InteriorTemperature.HasValue) _current.InteriorTemperature = dto.InteriorTemperature;
            if (dto.ExteriorTemperature.HasValue) _current.ExteriorTemperature = dto.ExteriorTemperature;
            if (dto.Speed.HasValue) _current.Speed = dto.Speed;
            if (dto.IsAvailable.HasValue) _current.IsAvailable = dto.IsAvailable;
        }
    }

    /// <summary>
    /// Carries out a command the way the car would, on the demo car's state: the command name and
    /// value as <see cref="VehicleCommands"/> checked them. Returns why the car refused, the way the
    /// gateway reports it, or null when it carried the command out.
    /// </summary>
    public string? ApplyCommand(string command, string value)
    {
        lock (_lock)
        {
            var refusal = Apply(_current, command, value);
            if (refusal is null)
            {
                _current.RecordedAt = DateTime.UtcNow;
                _current.LastVehicleStateAt = _current.RecordedAt;
            }
            return refusal;
        }
    }

    // The car's side of each command. Horn and lights and a refresh change nothing the dashboard
    // shows beyond the reading's time. Unlatching the boot pops it open; a power tailgate moves
    // either way.
    private static string? Apply(TelemetrySnapshot car, string command, string value)
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

    private static TelemetrySnapshot BuildDefaultSnapshot()
    {
        var current = DemoMockTrips.InProgressWaypoints[^1];
        var history = DemoHistory.Last;
        var drivingKm = DemoMockTrips.InProgressDistanceKm;
        var drivingKwh = drivingKm * DemoHistory.InProgressKwhPerKm;
        var soc = Math.Round(history.SocPercent - drivingKwh / DemoHistory.CapacityKwh * 100);

        return new()
        {
            Id = 1,
            VehicleId = 1,
            RecordedAt = DateTime.UtcNow,
            FuelLevelPercent = 68,
            FuelRangeKm = 420,
            EvSocPercent = soc,
            ElectricRangeKm = ElectricRangeAt(soc),
            HvSocKwh = Math.Round(soc / 100.0 * DemoHistory.CapacityKwh, 1),
            HvTotalCapacityKwh = DemoHistory.CapacityKwh,
            HvVoltage = 386.0,
            HvCurrent = 42.0,
            HvPower = 16.2,
            HvBatteryActive = true,
            OdometerKm = DemoMockTrips.CurrentOdometerKm,
            EngineRunning = true,
            IsCharging = false,
            ChargerConnected = false,
            ChargingType = "AC",
            ChargingCableLock = false,
            BmsChargeStatus = "UNPLUGGED",
            SocTargetPercent = 80,
            ChargeCurrentLimit = "Max",
            OnboardChargerPlugStatus = 0,
            OffboardChargerPlugStatus = 0,
            ObcVoltage = 0.0,
            ObcCurrent = 0.0,
            ObcPowerSinglePhase = 0.0,
            InteriorTemperature = 21.0,
            ExteriorTemperature = 14.0,
            RemoteTemperature = 19.5,
            Speed = current.SpeedKmh,
            Heading = 150,
            CurrentJourneyDistance = Math.Round(DemoMockTrips.InProgressDistanceKm, 1),
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
            TyrePressureFrontLeft = 2.4,
            TyrePressureFrontRight = 2.4,
            TyrePressureRearLeft = 2.5,
            TyrePressureRearRight = 2.26,
            Latitude = current.Lat,
            Longitude = current.Lon,
            Elevation = 3.0,
            BatteryVoltage = 12.7,
            LightsMainBeam = false,
            LightsDippedBeam = false,
            LightsSide = false,
            HeatedSeatFrontLeft = 0,
            HeatedSeatFrontRight = 0,
            RearWindowDefroster = false,
            IsAvailable = true,
            LastVehicleStateAt = DateTime.UtcNow,
            LastChargeStateAt = DateTime.UtcNow.AddHours(-8),
            MileageSinceLastCharge = Math.Round(history.SinceChargeKm + drivingKm, 1),
            MileageOfTheDay = Math.Round(history.DayKm + drivingKm, 1),
            PowerUsageOfDay = Math.Round(history.DayKwh + drivingKwh, 2),
            PowerUsageSinceLastCharge = Math.Round(history.SinceChargeKwh + drivingKwh, 2),
            ChargingScheduleMode = "UNTIL_CONFIGURED_TIME",
            ChargingScheduleStartTime = "00:00",
            ChargingScheduleEndTime = "07:00",
        };
    }

}
