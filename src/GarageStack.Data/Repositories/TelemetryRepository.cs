using System.Linq.Expressions;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

public class TelemetryRepository(AppDbContext db) : ITelemetryRepository
{
    private static readonly Expression<Func<TelemetrySnapshot, bool>> HasData =
        s => s.FuelLevelPercent != null || s.FuelRangeKm != null ||
             s.OdometerKm != null || s.EngineRunning != null || s.Speed != null ||
             s.IsLocked != null || s.ClimateOn != null ||
             s.DriverDoorOpen != null || s.PassengerDoorOpen != null ||
             s.RearLeftDoorOpen != null || s.RearRightDoorOpen != null ||
             s.TrunkOpen != null || s.BonnetOpen != null ||
             s.DriverWindowOpen != null || s.PassengerWindowOpen != null ||
             s.RearLeftWindowOpen != null || s.RearRightWindowOpen != null ||
             s.SunRoofOpen != null ||
             s.Latitude != null || s.Longitude != null || s.Heading != null ||
             s.BatteryVoltage != null ||
             s.InteriorTemperature != null || s.ExteriorTemperature != null ||
             s.RemoteTemperature != null ||
             s.EvSocPercent != null || s.IsCharging != null ||
             s.TyrePressureFrontLeft != null || s.TyrePressureFrontRight != null ||
             s.TyrePressureRearLeft != null || s.TyrePressureRearRight != null ||
             s.MileageOfTheDay != null || s.PowerUsageOfDay != null ||
             s.MileageSinceLastCharge != null ||
             s.HvVoltage != null || s.HvCurrent != null || s.HvPower != null ||
             s.HvSocKwh != null || s.HvTotalCapacityKwh != null ||
             s.PowerUsageSinceLastCharge != null ||
             s.ChargerConnected != null || s.HvBatteryActive != null ||
             s.LightsMainBeam != null || s.LightsDippedBeam != null || s.LightsSide != null ||
             s.HeatedSeatFrontLeft != null || s.HeatedSeatFrontRight != null ||
             s.RearWindowDefroster != null ||
             s.IsAvailable != null || s.LastVehicleStateAt != null || s.LastChargeStateAt != null ||
             s.CurrentJourneyDistance != null ||
             s.ChargingType != null || s.ChargingCableLock != null || s.RemainingChargingTime != null ||
             s.BmsChargeStatus != null || s.OnboardChargerPlugStatus != null || s.OffboardChargerPlugStatus != null ||
             s.LastChargeEndingPower != null || s.ChargingLastEndAt != null ||
             s.ChargingScheduleMode != null || s.ChargingScheduleStartTime != null || s.ChargingScheduleEndTime != null ||
             s.ObcCurrent != null || s.ObcVoltage != null || s.ObcPowerSinglePhase != null || s.ObcPowerThreePhase != null ||
             s.BatteryHeating != null || s.BatteryHeatingScheduleMode != null || s.BatteryHeatingScheduleStartTime != null ||
             s.Elevation != null;

    // Chart history excludes GPS-only rows: latitude/longitude arrive every minute during driving
    // and inflate the row count, causing the stride downsampler to skip the sparser fuel/EV/kWh rows.
    // GPS data for routes belongs to the trips endpoint, not chart history.
    private static readonly Expression<Func<TelemetrySnapshot, bool>> HasChartData =
        s => s.FuelLevelPercent != null || s.EvSocPercent != null ||
             s.PowerUsageOfDay != null || s.BatteryVoltage != null ||
             s.ClimateOn != null || s.IsCharging != null ||
             s.TyrePressureFrontLeft != null || s.TyrePressureFrontRight != null ||
             s.TyrePressureRearLeft != null || s.TyrePressureRearRight != null ||
             s.MileageOfTheDay != null || s.MileageSinceLastCharge != null ||
             s.HvSocKwh != null || s.HvTotalCapacityKwh != null ||
             s.PowerUsageSinceLastCharge != null;

    public async Task AddAsync(TelemetrySnapshot snapshot, CancellationToken ct = default)
    {
        db.TelemetrySnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
    }

    public Task<TelemetrySnapshot?> GetLatestAsync(int vehicleId, CancellationToken ct = default) =>
        db.TelemetrySnapshots
          .Where(s => s.VehicleId == vehicleId)
          .OrderByDescending(s => s.RecordedAt)
          .FirstOrDefaultAsync(ct);

    public async Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var rows = await db.TelemetrySnapshots
            .Where(s => s.VehicleId == vehicleId && s.RecordedAt >= since)
            .Where(HasData)
            .OrderByDescending(s => s.RecordedAt)
            .Take(200)
            .ToListAsync(ct);

        if (rows.Count == 0) return null;

        var todayStart = DateTime.UtcNow.Date;
        var merged = new TelemetrySnapshot { VehicleId = vehicleId, RecordedAt = rows[0].RecordedAt };
        foreach (var row in rows)
        {
            merged.FuelLevelPercent ??= row.FuelLevelPercent;
            merged.FuelRangeKm ??= row.FuelRangeKm;
            merged.OdometerKm ??= row.OdometerKm;
            merged.EngineRunning ??= row.EngineRunning;
            merged.Speed ??= row.Speed;
            merged.IsLocked ??= row.IsLocked;
            merged.ClimateOn ??= row.ClimateOn;
            merged.DriverDoorOpen ??= row.DriverDoorOpen;
            merged.PassengerDoorOpen ??= row.PassengerDoorOpen;
            merged.RearLeftDoorOpen ??= row.RearLeftDoorOpen;
            merged.RearRightDoorOpen ??= row.RearRightDoorOpen;
            merged.TrunkOpen ??= row.TrunkOpen;
            merged.BonnetOpen ??= row.BonnetOpen;
            merged.DriverWindowOpen ??= row.DriverWindowOpen;
            merged.PassengerWindowOpen ??= row.PassengerWindowOpen;
            merged.RearLeftWindowOpen ??= row.RearLeftWindowOpen;
            merged.RearRightWindowOpen ??= row.RearRightWindowOpen;
            merged.SunRoofOpen ??= row.SunRoofOpen;
            merged.Latitude ??= row.Latitude;
            merged.Longitude ??= row.Longitude;
            merged.Heading ??= row.Heading;
            merged.BatteryVoltage ??= row.BatteryVoltage;
            merged.InteriorTemperature ??= row.InteriorTemperature;
            merged.RemoteTemperature ??= row.RemoteTemperature;
            merged.ExteriorTemperature ??= row.ExteriorTemperature;
            merged.EvSocPercent ??= row.EvSocPercent;
            merged.IsCharging ??= row.IsCharging;
            merged.TyrePressureFrontLeft ??= row.TyrePressureFrontLeft;
            merged.TyrePressureFrontRight ??= row.TyrePressureFrontRight;
            merged.TyrePressureRearLeft ??= row.TyrePressureRearLeft;
            merged.TyrePressureRearRight ??= row.TyrePressureRearRight;
            // Daily counters are only meaningful from today - don't carry yesterday's values forward
            if (row.RecordedAt >= todayStart)
            {
                merged.MileageOfTheDay ??= row.MileageOfTheDay;
                merged.PowerUsageOfDay ??= row.PowerUsageOfDay;
            }
            merged.MileageSinceLastCharge ??= row.MileageSinceLastCharge;
            merged.HvVoltage ??= row.HvVoltage;
            merged.HvCurrent ??= row.HvCurrent;
            merged.HvPower ??= row.HvPower;
            merged.HvSocKwh ??= row.HvSocKwh;
            merged.HvTotalCapacityKwh ??= row.HvTotalCapacityKwh;
            merged.PowerUsageSinceLastCharge ??= row.PowerUsageSinceLastCharge;
            merged.ChargerConnected ??= row.ChargerConnected;
            merged.HvBatteryActive ??= row.HvBatteryActive;
            merged.LightsMainBeam ??= row.LightsMainBeam;
            merged.LightsDippedBeam ??= row.LightsDippedBeam;
            merged.LightsSide ??= row.LightsSide;
            merged.HeatedSeatFrontLeft ??= row.HeatedSeatFrontLeft;
            merged.HeatedSeatFrontRight ??= row.HeatedSeatFrontRight;
            merged.RearWindowDefroster ??= row.RearWindowDefroster;
            merged.IsAvailable ??= row.IsAvailable;
            merged.LastVehicleStateAt ??= row.LastVehicleStateAt;
            merged.LastChargeStateAt ??= row.LastChargeStateAt;
            merged.CurrentJourneyDistance ??= row.CurrentJourneyDistance;
            merged.ChargingType ??= row.ChargingType;
            merged.ChargingCableLock ??= row.ChargingCableLock;
            merged.RemainingChargingTime ??= row.RemainingChargingTime;
            merged.BmsChargeStatus ??= row.BmsChargeStatus;
            merged.OnboardChargerPlugStatus ??= row.OnboardChargerPlugStatus;
            merged.OffboardChargerPlugStatus ??= row.OffboardChargerPlugStatus;
            merged.LastChargeEndingPower ??= row.LastChargeEndingPower;
            merged.ChargingLastEndAt ??= row.ChargingLastEndAt;
            merged.ChargingScheduleMode ??= row.ChargingScheduleMode;
            merged.ChargingScheduleStartTime ??= row.ChargingScheduleStartTime;
            merged.ChargingScheduleEndTime ??= row.ChargingScheduleEndTime;
            merged.ObcCurrent ??= row.ObcCurrent;
            merged.ObcVoltage ??= row.ObcVoltage;
            merged.ObcPowerSinglePhase ??= row.ObcPowerSinglePhase;
            merged.ObcPowerThreePhase ??= row.ObcPowerThreePhase;
            merged.BatteryHeating ??= row.BatteryHeating;
            merged.BatteryHeatingScheduleMode ??= row.BatteryHeatingScheduleMode;
            merged.BatteryHeatingScheduleStartTime ??= row.BatteryHeatingScheduleStartTime;
            merged.Elevation ??= row.Elevation;
        }

        // GPS rows are sparse: the 200-row window may be filled with non-location
        // topics published while the car is parked. Fall back to the most recent
        // row that has coordinates - the partial index makes this cheap.
        if (merged.Latitude == null)
        {
            var loc = await db.TelemetrySnapshots
                .Where(s => s.VehicleId == vehicleId && s.Latitude != null && s.Longitude != null)
                .OrderByDescending(s => s.RecordedAt)
                .Select(s => new { s.Latitude, s.Longitude, s.Heading })
                .FirstOrDefaultAsync(ct);

            if (loc != null)
            {
                merged.Latitude = loc.Latitude;
                merged.Longitude = loc.Longitude;
                merged.Heading ??= loc.Heading;
            }
        }

        return merged;
    }

    public async Task<IReadOnlyList<TelemetrySnapshot>> GetHistoryAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var rows = await db.TelemetrySnapshots
            .AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.RecordedAt >= from && s.RecordedAt <= to)
            .Where(HasChartData)
            .OrderBy(s => s.RecordedAt)
            .ToListAsync(ct);

        if (rows.Count == 0) return rows;

        var maxPoints = (to - from).TotalDays switch
        {
            <= 1  => 288,  // ~5-min resolution
            <= 7  => 336,  // ~30-min resolution
            _     => 360,  // ~2-hour resolution
        };

        if (rows.Count <= maxPoints) return rows;

        // Ceiling division ensures step >= 2 whenever rows.Count > maxPoints,
        // so the result never exceeds maxPoints.
        var step = (rows.Count + maxPoints - 1) / maxPoints;
        var result = new List<TelemetrySnapshot>(maxPoints + 1);
        for (var i = 0; i < rows.Count; i += step)
            result.Add(rows[i]);
        return result;
    }

    public async Task<IReadOnlyList<TripDto>> GetTripsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        // Include speed=0 points so we can detect parking gaps between back-to-back trips.
        var points = await db.TelemetrySnapshots
            .Where(s => s.VehicleId == vehicleId && s.RecordedAt >= from && s.RecordedAt <= to
                        && s.Latitude != null && s.Longitude != null)
            .OrderBy(s => s.RecordedAt)
            .Select(s => new { s.RecordedAt, s.Latitude, s.Longitude, s.Speed })
            .ToListAsync(ct);

        if (points.Count == 0) return [];

        var trips = new List<TripDto>();
        var current = new List<TripPoint>();
        // Hard gap: no telemetry data at all for 30+ minutes.
        var gapThreshold = TimeSpan.FromMinutes(30);
        // Soft gap: car was stationary (speed=0) for 5+ minutes = distinct trip.
        var parkThreshold = TimeSpan.FromMinutes(5);
        DateTime? lastSeen = null;
        DateTime? parkingSince = null;

        foreach (var p in points)
        {
            var isParked = p.Speed.HasValue && p.Speed.Value <= 0;

            // Hard gap: no data at all - always start a new trip.
            if (lastSeen.HasValue && p.RecordedAt - lastSeen.Value > gapThreshold)
            {
                TryAddTrip(trips, current);
                current.Clear();
                parkingSince = null;
            }

            lastSeen = p.RecordedAt;

            if (isParked)
            {
                // Record when stationary period began; don't add to trip path.
                parkingSince ??= p.RecordedAt;
                continue;
            }

            // Moving point - split if parked long enough to count as a new trip.
            if (parkingSince.HasValue && p.RecordedAt - parkingSince.Value >= parkThreshold)
            {
                TryAddTrip(trips, current);
                current.Clear();
            }
            parkingSince = null;

            // Skip consecutive duplicate positions (GPS cached/not updating while driving).
            var pt = new TripPoint(p.RecordedAt, p.Latitude!.Value, p.Longitude!.Value, p.Speed);
            if (current.Count == 0 || !SamePosition(current[^1], pt))
                current.Add(pt);
        }

        TryAddTrip(trips, current);

        // Discard segments that never went anywhere (GPS drift, brief polling bursts while stationary).
        return trips.Where(t => t.DistanceKm >= 0.1).ToList();
    }

    private static void TryAddTrip(List<TripDto> trips, List<TripPoint> current)
    {
        if (current.Count < 2) return;
        // Pass a snapshot of current - the caller clears the list after this call, and
        // TripDto stores a reference, so without a copy all trips would end up sharing
        // the final segment's points.
        var trip = BuildTrip(trips.Count, new List<TripPoint>(current));
        if (trip is not null) trips.Add(trip);
    }

    // Two positions are considered identical when within ~1 metre of each other.
    private static bool SamePosition(TripPoint a, TripPoint b) =>
        Math.Abs(a.Latitude - b.Latitude) < 0.00001 &&
        Math.Abs(a.Longitude - b.Longitude) < 0.00001;

    private static TripDto? BuildTrip(int index, List<TripPoint> points)
    {
        var distance = 0.0;
        for (var i = 1; i < points.Count; i++)
            distance += Haversine(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);

        // Reject GPS-teleportation: consecutive points implying > 250 km/h are not real trips
        for (var i = 1; i < points.Count; i++)
        {
            var segKm = Haversine(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
            var segHours = (points[i].RecordedAt - points[i - 1].RecordedAt).TotalHours;
            if (segHours > 0 && segKm / segHours > 250)
                return null;
        }

        return new TripDto(index, points[0].RecordedAt, points[^1].RecordedAt, Math.Round(distance, 2), points.Count, points);
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
