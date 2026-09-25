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

    private static readonly Lazy<IReadOnlyList<TelemetrySnapshot>> _history =
        new(BuildHistory, LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<long> AddAsync(TelemetrySnapshot snapshot, CancellationToken ct = default) =>
        Task.FromResult(Interlocked.Increment(ref _nextId));

    public Task MergeIntoAsync(long rowId, TelemetrySnapshot patch, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<TelemetrySnapshot?> GetLatestAsync(int vehicleId, CancellationToken ct = default)
    {
        // Return a copy - callers must not be able to mutate the shared in-memory demo state.
        lock (_lock) { return Task.FromResult<TelemetrySnapshot?>(_current.Clone()); }
    }

    public Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult<TelemetrySnapshot?>(_current.Clone()); }
    }

    public Task<IReadOnlyList<TelemetryHistoryPoint>> GetHistoryAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TelemetryHistoryPoint>>(
            _history.Value
                .Where(s => s.RecordedAt >= from && s.RecordedAt <= to)
                .Select(TelemetryHistoryPoint.FromSnapshot)
                .ToList());

    // DemoTripRepository serves the demo's trips from DemoTrips directly, so nothing asks the demo
    // telemetry for the fixes to cut them from.
    public Task<IReadOnlyList<TripPoint>> GetGpsFixesAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TripPoint>>([]);

    public Task<DateTime?> GetFirstGpsFixAtAsync(int vehicleId, CancellationToken ct = default) =>
        Task.FromResult<DateTime?>(null);

    // Only the Worker's recorder asks, and demo mode runs no Worker: DemoTrips carries the readings.
    public Task<double?> GetOdometerAtAsync(int vehicleId, DateTime at, CancellationToken ct = default) =>
        Task.FromResult<double?>(null);

    // The in-progress trip is always the last entry (see DemoTrips), which is also the one the
    // live snapshot's CurrentJourneyDistance describes.
    public Task<LastTripSummary?> GetLastTripSummaryAsync(int vehicleId, CancellationToken ct = default)
    {
        var trip = DemoTrips.All[^1];
        return Task.FromResult<LastTripSummary?>(new LastTripSummary(trip.DistanceKm, trip.EndedAt));
    }

    // Demo data is not ingested from MQTT, so there are no raw topics to report.
    public Task<IReadOnlyList<RawTopicStat>> GetRawTopicStatsAsync(int vehicleId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RawTopicStat>>([]);

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
            if (dto.ClimateOn.HasValue) _current.ClimateOn = dto.ClimateOn;
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
            BmsChargeStatus = "NotCharging",
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
            TyrePressureRearLeft = 2.3,
            TyrePressureRearRight = 2.3,
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
            ChargingScheduleMode = "Immediate",
            ChargingScheduleStartTime = "00:00",
            ChargingScheduleEndTime = "07:00",
        };
    }

    private static IReadOnlyList<TelemetrySnapshot> BuildHistory()
    {
        var rng = new Random(42);
        var snapshots = new List<TelemetrySnapshot>(120);
        var baseDate = DateTime.UtcNow.Date.AddDays(-30);
        var odometer = 24300.0;
        var soc = 88.0;
        var fuel = 80.0;
        var idCounter = 100L;

        // 4 snapshots per day: depart (7h), midday (12h), return (17h), plug in (22h)
        var dayHours = new[] { 7, 12, 17, 22 };
        for (var day = 0; day < 30; day++)
        {
            for (var hi = 0; hi < dayHours.Length; hi++)
            {
                var hour = dayHours[hi];
                var ts = baseDate.AddDays(day).AddHours(hour).AddMinutes(rng.Next(0, 30));

                if (hour == 7)
                {
                    // after overnight charge
                    soc = 87 + rng.Next(-2, 8);
                }
                else if (hour == 12)
                {
                    var consumed = 6 + rng.Next(0, 5);
                    soc -= consumed;
                    fuel -= 1.5 + rng.NextDouble();
                    odometer += consumed * 7.2;
                }
                else if (hour == 17)
                {
                    var consumed = 5 + rng.Next(0, 5);
                    soc -= consumed;
                    fuel -= 1.0 + rng.NextDouble();
                    odometer += consumed * 7.2;
                }
                else if (hour == 22 && day % 7 == 0)
                {
                    // weekly refuel
                    fuel = 90 + rng.Next(0, 10);
                }

                soc = Math.Clamp(soc, 10, 97);
                fuel = Math.Clamp(fuel, 5, 100);

                var isCharging = hour == 22;
                var extTemp = 13.0 + (rng.NextDouble() - 0.5) * 10.0;
                var frontPressure = 2.4 + (rng.NextDouble() - 0.5) * 0.06;
                var rearPressure = 2.3 + (rng.NextDouble() - 0.5) * 0.06;

                snapshots.Add(new TelemetrySnapshot
                {
                    Id = idCounter++,
                    VehicleId = 1,
                    RecordedAt = ts,
                    FuelLevelPercent = Math.Round(fuel, 1),
                    FuelRangeKm = Math.Round(fuel / 100.0 * 650.0, 0),
                    EvSocPercent = Math.Round(soc, 1),
                    HvSocKwh = Math.Round(soc / 100.0 * 70.0, 1),
                    HvTotalCapacityKwh = 70.0,
                    OdometerKm = Math.Round(odometer, 1),
                    IsCharging = isCharging,
                    ChargerConnected = isCharging || hour == 7,
                    ExteriorTemperature = Math.Round(extTemp, 1),
                    InteriorTemperature = Math.Round(extTemp + rng.NextDouble() * 4.0, 1),
                    TyrePressureFrontLeft = Math.Round(frontPressure, 2),
                    TyrePressureFrontRight = Math.Round(frontPressure + (rng.NextDouble() - 0.5) * 0.04, 2),
                    TyrePressureRearLeft = Math.Round(rearPressure, 2),
                    TyrePressureRearRight = Math.Round(rearPressure + (rng.NextDouble() - 0.5) * 0.04, 2),
                    MileageOfTheDay = hour >= 12 ? Math.Round((odometer - 24300 - day * 90) % 200, 1) : 0,
                    BatteryVoltage = 12.6 + rng.NextDouble() * 0.3,
                    Latitude = 52.3676 + (rng.NextDouble() - 0.5) * 0.02,
                    Longitude = 4.9041 + (rng.NextDouble() - 0.5) * 0.02,
                    Speed = 0,
                    IsLocked = true,
                    EngineRunning = false,
                    HvBatteryActive = true,
                });
            }
        }

        return snapshots;
    }
}
