using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

public sealed class DemoTelemetryRepository : ITelemetryRepository
{
    private static long _nextId = 9000;
    private static readonly object _lock = new();

    // What the demo ZS EV covers on a full battery. The electric range is derived from the state of
    // charge, so the two stay in step when the demo panel moves the state of charge.
    private const double FullBatteryRangeKm = 400.0;

    private static double ElectricRangeAt(double socPercent) =>
        Math.Round(socPercent / 100.0 * FullBatteryRangeKm);

    private TelemetrySnapshot _current = BuildDefaultSnapshot();

    // A little over a year, so the statistics have something to show for last year and per month.
    private const int HistoryDays = 400;

    private const int SlowLeakDays = 12;
    private const double SlowLeakBarPerDay = 0.02;

    private static readonly Lazy<IReadOnlyList<TelemetrySnapshot>> _history =
        new(BuildHistory, LazyThreadSafetyMode.ExecutionAndPublication);

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
        foreach (var snapshot in _history.Value.OrderBy(s => s.RecordedAt))
            builder.Add(TelemetryHistoryPoint.FromSnapshot(snapshot));
        return Task.FromResult(builder.Build());
    }

    // DemoTripRepository serves the demo's trips from DemoTrips directly, so nothing asks the demo
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
        lock (_lock) { return Task.FromResult(_current.OdometerKm); }
    }

    // Only the Worker's parked alerts weigh past polls, and demo mode runs no Worker.
    public Task<IReadOnlyList<StatusReading>> GetStatusReadingsAsync(int vehicleId, int count, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StatusReading>>([]);

    public Task<string?> GetChargingStopReasonAsync(int vehicleId, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public async Task<IReadOnlyList<ChargeSession>> GetChargeSessionsAsync(int vehicleId, CancellationToken ct = default)
    {
        var readings = _history.Value.Select(ChargeSessionReading.FromSnapshot).ToList();
        if (await GetMergedLatestAsync(vehicleId, ct) is { } current)
            readings.Add(ChargeSessionReading.FromSnapshot(current));
        return ChargeSessionHistory.Build(readings);
    }

    // The generated history, oldest first, then the live snapshot the demo controls change.
    public async IAsyncEnumerable<TelemetrySnapshot> StreamAllAsync(
        int vehicleId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var snapshot in _history.Value.OrderBy(s => s.RecordedAt))
        {
            ct.ThrowIfCancellationRequested();
            yield return snapshot;
        }
        yield return (await GetMergedLatestAsync(vehicleId, ct))!;
    }

    public Task<PositionFix?> GetLatestPositionAsync(int vehicleId, CancellationToken ct = default)
    {
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
        var history = _history.Value.Where(s => s.RecordedAt >= from && s.RecordedAt <= to).ToList();
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
                _current.EvSocPercent = dto.EvSocPercent;
                _current.HvSocKwh = Math.Round(dto.EvSocPercent.Value / 100.0 * 70.0, 1);
                _current.ElectricRangeKm = ElectricRangeAt(dto.EvSocPercent.Value);
            }
            if (dto.InteriorTemperature.HasValue) _current.InteriorTemperature = dto.InteriorTemperature;
            if (dto.ExteriorTemperature.HasValue) _current.ExteriorTemperature = dto.ExteriorTemperature;
            if (dto.Speed.HasValue) _current.Speed = dto.Speed;
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
        var current = DemoTrips.InProgressWaypoints[^1];

        return new()
        {
            Id = 1,
            VehicleId = 1,
            RecordedAt = DateTime.UtcNow,
            FuelLevelPercent = 68,
            FuelRangeKm = 420,
            EvSocPercent = 71,
            ElectricRangeKm = ElectricRangeAt(71),
            HvSocKwh = Math.Round(71.0 / 100.0 * 70.0, 1),
            HvTotalCapacityKwh = 70.0,
            HvVoltage = 386.0,
            HvCurrent = 42.0,
            HvPower = 16.2,
            HvBatteryActive = true,
            OdometerKm = DemoTrips.CurrentOdometerKm,
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
            CurrentJourneyDistance = Math.Round(DemoTrips.InProgressDistanceKm, 1),
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
            // Where the slow leak in the history below has brought it: still above the low line.
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
            MileageSinceLastCharge = 32.4,
            MileageOfTheDay = 18.2,
            PowerUsageOfDay = 2.95,
            ChargingScheduleMode = "UNTIL_CONFIGURED_TIME",
            ChargingScheduleStartTime = "00:00",
            ChargingScheduleEndTime = "07:00",
        };
    }

    private static IReadOnlyList<TelemetrySnapshot> BuildHistory()
    {
        var rng = new Random(42);
        var snapshots = new List<TelemetrySnapshot>(HistoryDays * 10);
        var baseDate = DateTime.UtcNow.Date.AddDays(-HistoryDays);
        var odometer = 24300.0;
        var soc = 78.0;
        var fuel = 80.0;
        var usedTodayKwh = 0.0;
        var idCounter = 100L;
        var dayStartOdometer = odometer;
        var fastChargerLocations = new (double Latitude, double Longitude)[]
        {
            (52.3676, 4.9041),
            (52.0907, 5.1214),
            (51.9244, 4.4777),
            (52.0705, 4.3007),
        };

        void AddSnapshot(
            DateTime recordedAt,
            bool isCharging,
            string? chargingType = null,
            double? chargePowerKw = null,
            DateTime? chargeStartedAt = null,
            DateTime? chargeEndedAt = null,
            bool chargerConnected = false,
            double? latitude = null,
            double? longitude = null)
        {
            var extTemp = 13.0 + (rng.NextDouble() - 0.5) * 10.0;
            var frontPressure = 2.4 + (rng.NextDouble() - 0.5) * 0.06;
            var rearPressure = 2.5 + (rng.NextDouble() - 0.5) * 0.06;
            var day = (recordedAt.Date - baseDate).Days;
            // The rear right has a slow puncture: over the last twelve days it loses air the
            // other three keep, for the tyre history and the slow-puncture alert to show.
            var leak = Math.Max(0, day - (HistoryDays - SlowLeakDays)) * SlowLeakBarPerDay;

            snapshots.Add(new TelemetrySnapshot
            {
                Id = idCounter++,
                VehicleId = 1,
                RecordedAt = recordedAt,
                FuelLevelPercent = Math.Round(fuel, 1),
                FuelRangeKm = Math.Round(fuel / 100.0 * 650.0, 0),
                EvSocPercent = Math.Round(soc, 1),
                HvSocKwh = Math.Round(soc / 100.0 * 70.0, 1),
                HvTotalCapacityKwh = 70.0,
                PowerUsageOfDay = Math.Round(usedTodayKwh, 1),
                OdometerKm = Math.Round(odometer, 1),
                MileageOfTheDay = Math.Round(odometer - dayStartOdometer, 1),
                IsCharging = isCharging,
                ChargingType = chargingType,
                ChargingLastStartAt = chargeStartedAt,
                ChargingLastEndAt = chargeEndedAt,
                HvPower = isCharging && chargingType == "DC" ? -chargePowerKw * 1000 : null,
                ObcPowerSinglePhase = isCharging && chargingType == "AC" ? chargePowerKw * 1000 : null,
                ChargerConnected = chargerConnected,
                ExteriorTemperature = Math.Round(extTemp, 1),
                InteriorTemperature = Math.Round(extTemp + rng.NextDouble() * 4.0, 1),
                TyrePressureFrontLeft = Math.Round(frontPressure, 2),
                TyrePressureFrontRight = Math.Round(frontPressure + (rng.NextDouble() - 0.5) * 0.04, 2),
                TyrePressureRearLeft = Math.Round(rearPressure, 2),
                TyrePressureRearRight = Math.Round(rearPressure - leak + (rng.NextDouble() - 0.5) * 0.04, 2),
                BatteryVoltage = 12.6 + rng.NextDouble() * 0.3,
                Latitude = latitude ?? 52.3676 + (rng.NextDouble() - 0.5) * 0.02,
                Longitude = longitude ?? 4.9041 + (rng.NextDouble() - 0.5) * 0.02,
                Speed = 0,
                IsLocked = true,
                EngineRunning = false,
                HvBatteryActive = true,
            });
        }

        for (var day = 0; day < HistoryDays; day++)
        {
            var date = baseDate.AddDays(day);
            dayStartOdometer = odometer;
            usedTodayKwh = 0;
            AddSnapshot(date.AddHours(7), isCharging: false);

            foreach (var hour in new[] { 12, 17 })
            {
                // Typical urban trips consume about 18-20 kWh/100 km rather than changing the
                // battery by an arbitrary percentage each day.
                var tripEnergyKwh = 3.0 + rng.NextDouble() * 2.5;
                var efficiencyKwhPerKm = 0.18 + rng.NextDouble() * 0.02;
                soc = Math.Clamp(soc - tripEnergyKwh / 70.0 * 100.0, 10, 95);
                usedTodayKwh += tripEnergyKwh;
                odometer += tripEnergyKwh / efficiencyKwhPerKm;
                fuel = Math.Clamp(fuel - 1.0 - rng.NextDouble(), 5, 100);

                AddSnapshot(
                    date.AddHours(hour).AddMinutes(rng.Next(0, 30)),
                    isCharging: false);
            }

            if (day % 7 == 0)
            {
                // The demo plug-in hybrid also gets an occasional fuel stop.
                fuel = Math.Min(100, 90 + rng.Next(0, 10));
            }

            var shouldCharge = soc <= 45 || (soc <= 55 && rng.NextDouble() < 0.18);
            if (!shouldCharge)
            {
                AddSnapshot(date.AddHours(22).AddMinutes(rng.Next(0, 30)), isCharging: false);
                continue;
            }

            // Home AC is the usual case. A public DC stop is occasional and happens at a
            // different, fixed location rather than moving slightly on every sample.
            var isFastCharge = day % 28 == 0 || day % 28 == 1;
            var chargingType = isFastCharge ? "DC" : "AC";
            var chargeLocation = isFastCharge
                ? fastChargerLocations[(day / 28) % fastChargerLocations.Length]
                : (Latitude: 52.3812, Longitude: 4.9156);
            var targetSoc = isFastCharge ? 80.0 : rng.Next(0, 3) == 0 ? 85.0 : 80.0;
            var startSoc = soc;
            var chargeStartedAt = isFastCharge
                ? date.AddHours(18).AddMinutes(rng.Next(0, 5) * 15)
                : date.AddHours(19).AddMinutes(rng.Next(0, 7) * 30);
            var chargeAt = chargeStartedAt;
            var peakPowerKw = isFastCharge ? 75 + rng.NextDouble() * 12 : 6.8 + rng.NextDouble() * 0.7;
            var efficiency = isFastCharge ? 0.94 : 0.9;

            double ChargingPowerAt(double currentSoc, double acSupplyVariation = 1)
            {
                if (isFastCharge)
                {
                    var taper = Math.Clamp((targetSoc - currentSoc) / (targetSoc - 55), 0.2, 1);
                    return peakPowerKw * taper;
                }

                var progress = (currentSoc - startSoc) / (targetSoc - startSoc);
                var startupRamp = progress < 0.08 ? 0.82 + progress * 2.25 : 1;
                var highSocTaper = currentSoc > 80
                    ? Math.Clamp(1 - (currentSoc - 80) / 35, 0.82, 1)
                    : 1;
                return peakPowerKw * startupRamp * highSocTaper * acSupplyVariation;
            }

            AddSnapshot(
                chargeAt,
                isCharging: true,
                chargingType: chargingType,
                chargePowerKw: isFastCharge ? peakPowerKw : peakPowerKw * 0.45,
                chargeStartedAt: chargeStartedAt,
                chargerConnected: true,
                latitude: chargeLocation.Latitude,
                longitude: chargeLocation.Longitude);

            while (soc < targetSoc)
            {
                var supplyVariation = isFastCharge ? 1 : 0.98 + rng.NextDouble() * 0.04;
                var powerKw = ChargingPowerAt(soc, supplyVariation);
                var intervalMinutes = isFastCharge ? 5.0 : 10.0;
                var energyToTargetKwh = (targetSoc - soc) / 100.0 * 70.0;
                var minutesToTarget = energyToTargetKwh / (powerKw * efficiency) * 60.0;
                var elapsedMinutes = Math.Min(intervalMinutes, minutesToTarget);
                soc = Math.Min(targetSoc, soc + powerKw * efficiency * elapsedMinutes / 60.0 / 70.0 * 100.0);
                chargeAt = chargeAt.AddMinutes(elapsedMinutes);

                AddSnapshot(
                    chargeAt,
                    isCharging: true,
                    chargingType: chargingType,
                    chargePowerKw: ChargingPowerAt(soc, supplyVariation),
                    chargeStartedAt: chargeStartedAt,
                    chargerConnected: true,
                    latitude: chargeLocation.Latitude,
                    longitude: chargeLocation.Longitude);
            }

            // The charger stays connected briefly after the car reaches its target.
            var chargeEndedAt = chargeAt.AddMinutes(2);
            AddSnapshot(
                chargeEndedAt,
                isCharging: false,
                chargingType: chargingType,
                chargeEndedAt: chargeEndedAt,
                chargerConnected: true,
                latitude: chargeLocation.Latitude,
                longitude: chargeLocation.Longitude);
        }

        return snapshots;
    }
}
