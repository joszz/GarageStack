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

    public async Task<long> AddAsync(TelemetrySnapshot snapshot, CancellationToken ct = default)
    {
        db.TelemetrySnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
        return snapshot.Id;
    }

    public async Task MergeIntoAsync(long rowId, TelemetrySnapshot patch, CancellationToken ct = default)
    {
        var existing = await db.TelemetrySnapshots.FindAsync([rowId], ct);
        if (existing is null)
        {
            db.TelemetrySnapshots.Add(patch);
            await db.SaveChangesAsync(ct);
            return;
        }

        // Last-write-wins per field: overwrite with any non-null value from the patch.
        if (patch.FuelLevelPercent != null) existing.FuelLevelPercent = patch.FuelLevelPercent;
        if (patch.FuelRangeKm != null) existing.FuelRangeKm = patch.FuelRangeKm;
        if (patch.OdometerKm != null) existing.OdometerKm = patch.OdometerKm;
        if (patch.IsLocked != null) existing.IsLocked = patch.IsLocked;
        if (patch.EngineRunning != null) existing.EngineRunning = patch.EngineRunning;
        if (patch.ClimateOn != null) existing.ClimateOn = patch.ClimateOn;
        if (patch.DriverDoorOpen != null) existing.DriverDoorOpen = patch.DriverDoorOpen;
        if (patch.PassengerDoorOpen != null) existing.PassengerDoorOpen = patch.PassengerDoorOpen;
        if (patch.RearLeftDoorOpen != null) existing.RearLeftDoorOpen = patch.RearLeftDoorOpen;
        if (patch.RearRightDoorOpen != null) existing.RearRightDoorOpen = patch.RearRightDoorOpen;
        if (patch.TrunkOpen != null) existing.TrunkOpen = patch.TrunkOpen;
        if (patch.BonnetOpen != null) existing.BonnetOpen = patch.BonnetOpen;
        if (patch.DriverWindowOpen != null) existing.DriverWindowOpen = patch.DriverWindowOpen;
        if (patch.PassengerWindowOpen != null) existing.PassengerWindowOpen = patch.PassengerWindowOpen;
        if (patch.RearLeftWindowOpen != null) existing.RearLeftWindowOpen = patch.RearLeftWindowOpen;
        if (patch.RearRightWindowOpen != null) existing.RearRightWindowOpen = patch.RearRightWindowOpen;
        if (patch.SunRoofOpen != null) existing.SunRoofOpen = patch.SunRoofOpen;
        if (patch.Latitude != null) existing.Latitude = patch.Latitude;
        if (patch.Longitude != null) existing.Longitude = patch.Longitude;
        if (patch.Speed != null) existing.Speed = patch.Speed;
        if (patch.Heading != null) existing.Heading = patch.Heading;
        if (patch.BatteryVoltage != null) existing.BatteryVoltage = patch.BatteryVoltage;
        if (patch.InteriorTemperature != null) existing.InteriorTemperature = patch.InteriorTemperature;
        if (patch.ExteriorTemperature != null) existing.ExteriorTemperature = patch.ExteriorTemperature;
        if (patch.RemoteTemperature != null) existing.RemoteTemperature = patch.RemoteTemperature;
        if (patch.EvSocPercent != null) existing.EvSocPercent = patch.EvSocPercent;
        if (patch.IsCharging != null) existing.IsCharging = patch.IsCharging;
        if (patch.TyrePressureFrontLeft != null) existing.TyrePressureFrontLeft = patch.TyrePressureFrontLeft;
        if (patch.TyrePressureFrontRight != null) existing.TyrePressureFrontRight = patch.TyrePressureFrontRight;
        if (patch.TyrePressureRearLeft != null) existing.TyrePressureRearLeft = patch.TyrePressureRearLeft;
        if (patch.TyrePressureRearRight != null) existing.TyrePressureRearRight = patch.TyrePressureRearRight;
        if (patch.MileageOfTheDay != null) existing.MileageOfTheDay = patch.MileageOfTheDay;
        if (patch.PowerUsageOfDay != null) existing.PowerUsageOfDay = patch.PowerUsageOfDay;
        if (patch.MileageSinceLastCharge != null) existing.MileageSinceLastCharge = patch.MileageSinceLastCharge;
        if (patch.HvVoltage != null) existing.HvVoltage = patch.HvVoltage;
        if (patch.HvCurrent != null) existing.HvCurrent = patch.HvCurrent;
        if (patch.HvPower != null) existing.HvPower = patch.HvPower;
        if (patch.HvSocKwh != null) existing.HvSocKwh = patch.HvSocKwh;
        if (patch.HvTotalCapacityKwh != null) existing.HvTotalCapacityKwh = patch.HvTotalCapacityKwh;
        if (patch.PowerUsageSinceLastCharge != null) existing.PowerUsageSinceLastCharge = patch.PowerUsageSinceLastCharge;
        if (patch.ChargerConnected != null) existing.ChargerConnected = patch.ChargerConnected;
        if (patch.HvBatteryActive != null) existing.HvBatteryActive = patch.HvBatteryActive;
        if (patch.LightsMainBeam != null) existing.LightsMainBeam = patch.LightsMainBeam;
        if (patch.LightsDippedBeam != null) existing.LightsDippedBeam = patch.LightsDippedBeam;
        if (patch.LightsSide != null) existing.LightsSide = patch.LightsSide;
        if (patch.HeatedSeatFrontLeft != null) existing.HeatedSeatFrontLeft = patch.HeatedSeatFrontLeft;
        if (patch.HeatedSeatFrontRight != null) existing.HeatedSeatFrontRight = patch.HeatedSeatFrontRight;
        if (patch.RearWindowDefroster != null) existing.RearWindowDefroster = patch.RearWindowDefroster;
        if (patch.IsAvailable != null) existing.IsAvailable = patch.IsAvailable;
        if (patch.LastVehicleStateAt != null) existing.LastVehicleStateAt = patch.LastVehicleStateAt;
        if (patch.LastChargeStateAt != null) existing.LastChargeStateAt = patch.LastChargeStateAt;
        if (patch.CurrentJourneyDistance != null) existing.CurrentJourneyDistance = patch.CurrentJourneyDistance;
        if (patch.Elevation != null) existing.Elevation = patch.Elevation;
        if (patch.ChargingType != null) existing.ChargingType = patch.ChargingType;
        if (patch.ChargingCableLock != null) existing.ChargingCableLock = patch.ChargingCableLock;
        if (patch.RemainingChargingTime != null) existing.RemainingChargingTime = patch.RemainingChargingTime;
        if (patch.BmsChargeStatus != null) existing.BmsChargeStatus = patch.BmsChargeStatus;
        if (patch.OnboardChargerPlugStatus != null) existing.OnboardChargerPlugStatus = patch.OnboardChargerPlugStatus;
        if (patch.OffboardChargerPlugStatus != null) existing.OffboardChargerPlugStatus = patch.OffboardChargerPlugStatus;
        if (patch.LastChargeEndingPower != null) existing.LastChargeEndingPower = patch.LastChargeEndingPower;
        if (patch.ChargingLastEndAt != null) existing.ChargingLastEndAt = patch.ChargingLastEndAt;
        if (patch.ChargingScheduleMode != null) existing.ChargingScheduleMode = patch.ChargingScheduleMode;
        if (patch.ChargingScheduleStartTime != null) existing.ChargingScheduleStartTime = patch.ChargingScheduleStartTime;
        if (patch.ChargingScheduleEndTime != null) existing.ChargingScheduleEndTime = patch.ChargingScheduleEndTime;
        if (patch.ObcCurrent != null) existing.ObcCurrent = patch.ObcCurrent;
        if (patch.ObcVoltage != null) existing.ObcVoltage = patch.ObcVoltage;
        if (patch.ObcPowerSinglePhase != null) existing.ObcPowerSinglePhase = patch.ObcPowerSinglePhase;
        if (patch.ObcPowerThreePhase != null) existing.ObcPowerThreePhase = patch.ObcPowerThreePhase;
        if (patch.BatteryHeating != null) existing.BatteryHeating = patch.BatteryHeating;
        if (patch.BatteryHeatingScheduleMode != null) existing.BatteryHeatingScheduleMode = patch.BatteryHeatingScheduleMode;
        if (patch.BatteryHeatingScheduleStartTime != null) existing.BatteryHeatingScheduleStartTime = patch.BatteryHeatingScheduleStartTime;

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

        // If the MG API still reports a journey distance but the engine is off,
        // the trip has ended and the firmware just hasn't cleared the field yet.
        // The 5-minute guard on speed handles the red-light case (speed=0 but
        // engine still running - no EngineRunning row arrives during a stop).
        if (merged.CurrentJourneyDistance is > 0)
        {
            var lastSpeedRow = rows.FirstOrDefault(r => r.Speed != null);
            var engineOff  = merged.EngineRunning == false;
            var stationary = lastSpeedRow is { Speed: <= 0 } && DateTime.UtcNow - lastSpeedRow.RecordedAt > TimeSpan.FromMinutes(5);
            if (engineOff || stationary)
                merged.CurrentJourneyDistance = null;
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

        // Charging/heating schedule fields are set only when the user changes a schedule
        // and may not appear in the most-recent 200 rows. Fall back to the last row
        // that holds any scheduling data so the dashboard keeps showing those cards.
        if (merged.ChargingScheduleMode == null && merged.ChargingScheduleStartTime == null
            && merged.BatteryHeatingScheduleMode == null && merged.BatteryHeatingScheduleStartTime == null)
        {
            var sched = await db.TelemetrySnapshots
                .Where(s => s.VehicleId == vehicleId
                    && (s.ChargingScheduleMode != null || s.ChargingScheduleStartTime != null
                        || s.ChargingScheduleEndTime != null || s.BatteryHeatingScheduleMode != null
                        || s.BatteryHeatingScheduleStartTime != null))
                .OrderByDescending(s => s.RecordedAt)
                .Select(s => new
                {
                    s.ChargingScheduleMode, s.ChargingScheduleStartTime, s.ChargingScheduleEndTime,
                    s.BatteryHeatingScheduleMode, s.BatteryHeatingScheduleStartTime,
                })
                .FirstOrDefaultAsync(ct);

            if (sched != null)
            {
                merged.ChargingScheduleMode ??= sched.ChargingScheduleMode;
                merged.ChargingScheduleStartTime ??= sched.ChargingScheduleStartTime;
                merged.ChargingScheduleEndTime ??= sched.ChargingScheduleEndTime;
                merged.BatteryHeatingScheduleMode ??= sched.BatteryHeatingScheduleMode;
                merged.BatteryHeatingScheduleStartTime ??= sched.BatteryHeatingScheduleStartTime;
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

        // Per-day downsampling: each calendar day gets its own stride so that
        // the sampler cannot systematically skip a particular MQTT field type.
        // (A global stride whose step aligns with the per-poll batch size causes
        // every sample to land on the same field type, e.g. always batteryVoltage,
        // leaving fuelLevelPercent/evSocPercent blank for most days.)
        var dayGroups = rows
            .GroupBy(r => r.RecordedAt.Date)
            .OrderBy(g => g.Key)
            .ToList();

        var targetPerDay = Math.Max(1, maxPoints / dayGroups.Count);
        var result = new List<TelemetrySnapshot>(maxPoints + dayGroups.Count);
        foreach (var dayGroup in dayGroups)
        {
            var dayRows = dayGroup.ToList();
            if (dayRows.Count <= targetPerDay)
            {
                result.AddRange(dayRows);
                continue;
            }
            // Even floating-point spacing avoids GCD aliasing: an integer stride
            // whose GCD with the MQTT batch cycle size (typically 9 rows/poll) is
            // > 1 causes the sampler to systematically skip certain field types
            // (e.g. always landing on batteryVoltage, never on fuelLevelPercent).
            var spacing = (double)dayRows.Count / targetPerDay;
            for (var k = 0; k < targetPerDay; k++)
                result.Add(dayRows[(int)(k * spacing)]);
        }
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
        // Soft gap: car was stationary for 5+ minutes = distinct trip.
        var parkThreshold = TimeSpan.FromMinutes(5);
        DateTime? lastSeen = null;
        DateTime? parkingSince = null;
        // Last known GPS position, used to detect stationary GPS-only rows.
        double? prevLat = null, prevLon = null;

        foreach (var p in points)
        {
            // GPS rows from the location/position MQTT topic never carry Speed
            // (speed arrives on a separate topic in a separate DB row).  Treat
            // null-speed rows as stationary only when the position hasn't moved
            // significantly from the last point - i.e. GPS drift rather than
            // actual movement.  ~50 m is well above GPS noise but well below
            // any real movement between consecutive updates.
            bool isParked;
            if (p.Speed.HasValue)
            {
                isParked = p.Speed.Value <= 0;
            }
            else
            {
                isParked = prevLat.HasValue &&
                           Haversine(prevLat.Value, prevLon!.Value,
                                     p.Latitude!.Value, p.Longitude!.Value) * 1000 <= 50;
            }

            // Hard gap: no data at all - always start a new trip.
            if (lastSeen.HasValue && p.RecordedAt - lastSeen.Value > gapThreshold)
            {
                TryAddTrip(trips, current);
                current.Clear();
                parkingSince = null;
                prevLat = null;
                prevLon = null;
            }

            lastSeen = p.RecordedAt;

            if (isParked)
            {
                // Record when stationary period began; don't add to trip path.
                parkingSince ??= p.RecordedAt;
                prevLat = p.Latitude!.Value;
                prevLon = p.Longitude!.Value;
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

            prevLat = p.Latitude!.Value;
            prevLon = p.Longitude!.Value;
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
