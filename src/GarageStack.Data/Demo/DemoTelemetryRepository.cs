using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

public sealed class DemoTelemetryRepository : ITelemetryRepository
{
    private static long _nextId = 9000;
    private static readonly object _lock = new();

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
        lock (_lock) { return Task.FromResult<TelemetrySnapshot?>(_current); }
    }

    public Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult<TelemetrySnapshot?>(_current); }
    }

    public Task<IReadOnlyList<TelemetrySnapshot>> GetHistoryAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TelemetrySnapshot>>(
            _history.Value.Where(s => s.RecordedAt >= from && s.RecordedAt <= to).ToList());

    public Task<IReadOnlyList<TripDto>> GetTripsAsync(
        int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TripDto>>(
            _allTrips.Value.Where(t => t.StartedAt >= from && t.StartedAt <= to).ToList());

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
        }
    }

    private static TelemetrySnapshot BuildDefaultSnapshot() => new()
    {
        Id = 1,
        VehicleId = 1,
        RecordedAt = DateTime.UtcNow.AddMinutes(-3),
        EvSocPercent = 78,
        HvSocKwh = Math.Round(78.0 / 100.0 * 70.0, 1),
        HvTotalCapacityKwh = 70.0,
        HvVoltage = 386.0,
        HvCurrent = 0.0,
        HvPower = 0.0,
        HvBatteryActive = true,
        OdometerKm = 24852,
        EngineRunning = false,
        IsCharging = false,
        ChargerConnected = true,
        ChargingType = "AC",
        ChargingCableLock = true,
        BmsChargeStatus = "FullyCharged",
        OnboardChargerPlugStatus = 1,
        OffboardChargerPlugStatus = 0,
        ObcVoltage = 230.0,
        ObcCurrent = 0.0,
        ObcPowerSinglePhase = 0.0,
        InteriorTemperature = 21.0,
        ExteriorTemperature = 14.0,
        RemoteTemperature = 19.5,
        Speed = 0,
        Heading = 270,
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
        Latitude = 52.3676,
        Longitude = 4.9041,
        Elevation = 3.0,
        BatteryVoltage = 12.7,
        LightsMainBeam = false,
        LightsDippedBeam = false,
        LightsSide = false,
        HeatedSeatFrontLeft = 0,
        HeatedSeatFrontRight = 0,
        RearWindowDefroster = false,
        IsAvailable = true,
        LastVehicleStateAt = DateTime.UtcNow.AddMinutes(-3),
        LastChargeStateAt = DateTime.UtcNow.AddHours(-8),
        MileageSinceLastCharge = 32.4,
        MileageOfTheDay = 18.2,
        PowerUsageOfDay = 2950,
        ChargingScheduleMode = "Immediate",
        ChargingScheduleStartTime = "00:00",
        ChargingScheduleEndTime = "07:00",
    };

    private static IReadOnlyList<TelemetrySnapshot> BuildHistory()
    {
        var rng = new Random(42);
        var snapshots = new List<TelemetrySnapshot>(120);
        var baseDate = DateTime.UtcNow.Date.AddDays(-30);
        var odometer = 24300.0;
        var soc = 88.0;
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
                    odometer += consumed * 7.2;
                }
                else if (hour == 17)
                {
                    var consumed = 5 + rng.Next(0, 5);
                    soc -= consumed;
                    odometer += consumed * 7.2;
                }

                soc = Math.Clamp(soc, 10, 97);

                var isCharging = hour == 22;
                var extTemp = 13.0 + (rng.NextDouble() - 0.5) * 10.0;
                var frontPressure = 2.4 + (rng.NextDouble() - 0.5) * 0.06;
                var rearPressure = 2.3 + (rng.NextDouble() - 0.5) * 0.06;

                snapshots.Add(new TelemetrySnapshot
                {
                    Id = idCounter++,
                    VehicleId = 1,
                    RecordedAt = ts,
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
            BuildTrip(0, now.AddDays(-2).AddHours(8), "Amsterdam to Schiphol",
            [
                (52.3780, 4.9003, 35),
                (52.3621, 4.8879, 55),
                (52.3480, 4.8750, 90),
                (52.3380, 4.8450, 95),
                (52.3280, 4.8100, 90),
                (52.3200, 4.7850, 70),
                (52.3090, 4.7649, 30),
            ]),
            BuildTrip(1, now.AddDays(-5).AddHours(9), "Amsterdam to Haarlem",
            [
                (52.3676, 4.9041, 30),
                (52.3700, 4.8700, 80),
                (52.3740, 4.8100, 90),
                (52.3780, 4.7700, 90),
                (52.3830, 4.7200, 80),
                (52.3860, 4.6850, 60),
                (52.3874, 4.6462, 20),
            ]),
            BuildTrip(2, now.AddDays(-8).AddHours(14), "City drive",
            [
                (52.3676, 4.9041, 25),
                (52.3710, 4.9150, 30),
                (52.3740, 4.9220, 25),
                (52.3750, 4.9100, 20),
                (52.3730, 4.8960, 25),
                (52.3700, 4.8980, 20),
                (52.3676, 4.9041, 10),
            ]),
            BuildTrip(3, now.AddDays(-12).AddHours(10), "Amsterdam to Utrecht",
            [
                (52.3676, 4.9041, 40),
                (52.3400, 4.9200, 100),
                (52.3000, 4.9400, 110),
                (52.2500, 4.9600, 120),
                (52.2000, 4.9800, 120),
                (52.1500, 5.0100, 110),
                (52.1100, 5.0700, 90),
                (52.0907, 5.1214, 30),
            ]),
            BuildTrip(4, now.AddDays(-18).AddHours(16), "Amsterdam to Almere",
            [
                (52.3676, 4.9041, 35),
                (52.3700, 4.9500, 90),
                (52.3710, 5.0100, 100),
                (52.3705, 5.0700, 100),
                (52.3700, 5.1300, 90),
                (52.3700, 5.1900, 60),
                (52.3702, 5.2158, 25),
            ]),
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
                var seg = Haversine(
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

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
    }
}
