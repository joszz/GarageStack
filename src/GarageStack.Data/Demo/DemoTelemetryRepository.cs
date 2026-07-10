using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

public sealed class DemoTelemetryRepository : ITelemetryRepository
{
    private static long _nextId = 9000;
    private static readonly object _lock = new();

    // The vehicle is mid-trip in the demo scenario: this is the road already driven, ending at
    // the "live" position reported by BuildDefaultSnapshot. Kept as one source so the current
    // status marker and the in-progress trip's route line agree on where the car is.
    private static readonly (double Lat, double Lon, double SpeedKmh)[] InProgressTripWaypoints =
    [
        (52.3676, 4.9041, 30),
        (52.3600, 4.9143, 45),
        (52.3520, 4.9210, 55),
        (52.3455, 4.9330, 60),
        (52.3401, 4.9455, 65),
    ];

    private static double TotalDistanceKm((double Lat, double Lon, double SpeedKmh)[] waypoints)
    {
        var total = 0.0;
        for (var i = 1; i < waypoints.Length; i++)
            total += GeoHelper.Haversine(waypoints[i - 1].Lat, waypoints[i - 1].Lon, waypoints[i].Lat, waypoints[i].Lon);
        return total;
    }

    private TelemetrySnapshot _current = BuildDefaultSnapshot();

    private static readonly Lazy<IReadOnlyList<TelemetrySnapshot>> _history =
        new(BuildHistory, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlyList<TripDto>> _allTrips =
        new(BuildTrips, LazyThreadSafetyMode.ExecutionAndPublication);

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

    public Task<IReadOnlyList<TelemetrySnapshot>> GetHistoryAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TelemetrySnapshot>>(
            _history.Value.Where(s => s.RecordedAt >= from && s.RecordedAt <= to).ToList());

    public Task<IReadOnlyList<TripDto>> GetTripsAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TripDto>>(
            _allTrips.Value.Where(t => t.StartedAt >= from && t.StartedAt <= to).ToList());

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
            }
            if (dto.InteriorTemperature.HasValue) _current.InteriorTemperature = dto.InteriorTemperature;
            if (dto.ExteriorTemperature.HasValue) _current.ExteriorTemperature = dto.ExteriorTemperature;
            if (dto.Speed.HasValue) _current.Speed = dto.Speed;
        }
    }

    private static TelemetrySnapshot BuildDefaultSnapshot()
    {
        var current = InProgressTripWaypoints[^1];

        return new()
        {
            Id = 1,
            VehicleId = 1,
            RecordedAt = DateTime.UtcNow,
            FuelLevelPercent = 68,
            FuelRangeKm = 420,
            EvSocPercent = 71,
            HvSocKwh = Math.Round(71.0 / 100.0 * 70.0, 1),
            HvTotalCapacityKwh = 70.0,
            HvVoltage = 386.0,
            HvCurrent = 42.0,
            HvPower = 16.2,
            HvBatteryActive = true,
            OdometerKm = 24852,
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
            CurrentJourneyDistance = Math.Round(TotalDistanceKm(InProgressTripWaypoints), 1),
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
            PowerUsageOfDay = 2950,
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

    private static IReadOnlyList<TripDto> BuildTrips()
    {
        var now = DateTime.UtcNow;
        return
        [
            // All waypoints sourced from OSRM road routing — coordinates follow real roads.
            BuildTrip(0, now.AddDays(-2).AddHours(8), "Amsterdam to Schiphol",
            [
                (52.3768, 4.9006, 30),
                (52.3783, 4.9049, 40),
                (52.3830, 4.8933, 70),
                (52.3931, 4.8756, 90),
                (52.3919, 4.8434, 90),
                (52.3807, 4.8447, 80),
                (52.3727, 4.8421, 90),
                (52.3525, 4.8425, 100),
                (52.3402, 4.8408, 110),
                (52.3381, 4.8128, 110),
                (52.3279, 4.7784, 110),
                (52.3078, 4.7471, 90),
                (52.3090, 4.7635, 30),
            ]),
            BuildTrip(1, now.AddDays(-5).AddHours(9), "Amsterdam to Haarlem",
            [
                (52.3676, 4.9041, 25),
                (52.3625, 4.9071, 40),
                (52.3501, 4.9162, 75),
                (52.3461, 4.9281, 90),
                (52.3383, 4.9392, 90),
                (52.3375, 4.8908, 80),
                (52.3381, 4.8471, 90),
                (52.3609, 4.7328, 100),
                (52.3725, 4.7111, 80),
                (52.3831, 4.7081, 50),
                (52.3871, 4.6458, 20),
            ]),
            BuildTrip(2, now.AddDays(-8).AddHours(14), "City drive",
            [
                (52.3676, 4.9041, 25),
                (52.3618, 4.9075, 30),
                (52.3487, 4.9182, 50),
                (52.3383, 4.9392, 55),
                (52.3469, 4.9269, 50),
                (52.3496, 4.9170, 45),
                (52.3778, 4.9082, 35),
                (52.3702, 4.8958, 30),
                (52.3676, 4.9041, 20),
            ]),
            BuildTrip(3, now.AddDays(-12).AddHours(10), "Amsterdam to Utrecht",
            [
                (52.3676, 4.9041, 30),
                (52.3516, 4.9137, 55),
                (52.3383, 4.9393, 90),
                (52.3275, 4.9103, 90),
                (52.2753, 4.9562, 110),
                (52.2236, 4.9851, 120),
                (52.1663, 4.9872, 120),
                (52.1324, 5.0101, 110),
                (52.1171, 5.0329, 100),
                (52.1283, 5.0438, 90),
                (52.1365, 5.0796, 90),
                (52.1277, 5.1056, 90),
                (52.1183, 5.1283, 70),
                (52.1137, 5.1220, 60),
                (52.0907, 5.1215, 30),
            ]),
            BuildTrip(4, now.AddDays(-18).AddHours(16), "Amsterdam to Almere",
            [
                (52.3676, 4.9041, 30),
                (52.3516, 4.9137, 60),
                (52.3482, 4.9249, 90),
                (52.3365, 4.9429, 90),
                (52.3402, 4.9520, 90),
                (52.3493, 4.9617, 100),
                (52.3453, 4.9776, 110),
                (52.3337, 4.9992, 110),
                (52.3323, 5.0193, 110),
                (52.3229, 5.0670, 110),
                (52.3144, 5.1092, 100),
                (52.3215, 5.1312, 90),
                (52.3342, 5.1572, 90),
                (52.3477, 5.1903, 80),
                (52.3686, 5.2045, 60),
                (52.3702, 5.2159, 30),
            ]),
            BuildTrip(5, now.AddDays(-3).AddHours(17), "Den Haag to Delft",
            [
                (52.0707, 4.3008, 20),
                (52.0674, 4.3034, 55),
                (52.0641, 4.3107, 80),
                (52.0505, 4.3137, 90),
                (52.0346, 4.3288, 75),
                (52.0291, 4.3372, 70),
                (52.0227, 4.3471, 55),
                (52.0117, 4.3573, 20),
            ]),
            BuildTrip(6, now.AddDays(-7).AddHours(8), "Zaandam to Amsterdam",
            [
                (52.4379, 4.8250, 20),
                (52.4296, 4.8254, 50),
                (52.4282, 4.8358, 65),
                (52.4310, 4.8552, 75),
                (52.4316, 4.8629, 80),
                (52.4265, 4.8758, 80),
                (52.4221, 4.9044, 75),
                (52.4183, 4.9129, 70),
                (52.3840, 4.9108, 80),
                (52.3743, 4.9122, 60),
                (52.3676, 4.9041, 25),
            ]),
            BuildTrip(7, now.AddDays(-20).AddHours(10), "Amsterdam to Amstelveen",
            [
                (52.3676, 4.9041, 25),
                (52.3516, 4.9137, 55),
                (52.3482, 4.9249, 80),
                (52.3382, 4.9393, 80),
                (52.3309, 4.9244, 80),
                (52.3288, 4.9165, 80),
                (52.3185, 4.9168, 80),
                (52.3110, 4.9244, 80),
                (52.2989, 4.9086, 70),
                (52.2976, 4.8944, 60),
                (52.3003, 4.8596, 25),
            ]),
            BuildTrip(8, now.AddDays(-25).AddHours(14), "Utrecht to Amersfoort",
            [
                (52.0907, 5.1215, 25),
                (52.0931, 5.1367, 50),
                (52.0930, 5.1452, 70),
                (52.0917, 5.1621, 80),
                (52.0924, 5.1803, 90),
                (52.0931, 5.2011, 100),
                (52.1038, 5.2363, 100),
                (52.1093, 5.2733, 100),
                (52.1154, 5.3030, 90),
                (52.1229, 5.3404, 80),
                (52.1281, 5.3635, 70),
                (52.1561, 5.3878, 25),
            ]),
            BuildTrip(9, now.AddDays(-35).AddHours(11), "Tilburg to Breda",
            [
                (51.5556, 5.0915, 25),
                (51.5380, 5.0634, 80),
                (51.5399, 5.0309, 90),
                (51.5383, 4.9994, 100),
                (51.5505, 4.9631, 100),
                (51.5528, 4.9313, 100),
                (51.5566, 4.9008, 100),
                (51.5578, 4.8635, 90),
                (51.5524, 4.8343, 80),
                (51.5604, 4.8206, 60),
                (51.5719, 4.7683, 25),
            ]),
            // In progress: the road already driven, ending at BuildDefaultSnapshot's live position.
            // Must stay last in this list - the API returns trips in list order and the frontend
            // treats the final entry as the active trip whenever CurrentJourneyDistance > 0.
            BuildTrip(10, now.AddMinutes(-8), "Amsterdam to Duivendrecht", InProgressTripWaypoints),
        ];
    }

    private static TripDto BuildTrip(int index, DateTime start, string _,
        IReadOnlyList<(double Lat, double Lon, double SpeedKmh)> waypoints)
    {
        var points = new List<TripPoint>(waypoints.Count);
        var totalKm = 0.0;
        var minutesElapsed = 0.0;

        for (var i = 0; i < waypoints.Count; i++)
        {
            if (i > 0)
            {
                var seg = GeoHelper.Haversine(
                    waypoints[i - 1].Lat, waypoints[i - 1].Lon,
                    waypoints[i].Lat, waypoints[i].Lon);
                totalKm += seg;
                var avgSpeed = (waypoints[i - 1].SpeedKmh + waypoints[i].SpeedKmh) / 2.0;
                minutesElapsed += avgSpeed > 0 ? seg / avgSpeed * 60.0 : 2.0;
            }
            points.Add(new TripPoint(
                start.AddMinutes(minutesElapsed),
                waypoints[i].Lat,
                waypoints[i].Lon,
                waypoints[i].SpeedKmh));
        }

        var endedAt = points[^1].RecordedAt;
        return new TripDto(index, start, endedAt, Math.Round(totalKm, 1), points.Count, points);
    }
}
