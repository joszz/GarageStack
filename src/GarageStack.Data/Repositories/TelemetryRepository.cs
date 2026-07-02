using System.Linq.Expressions;
using System.Reflection;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Repositories;

// logger is optional (DI always supplies one) so existing tests can keep constructing
// this directly with just a DbContext.
public class TelemetryRepository(AppDbContext db, ILogger<TelemetryRepository>? logger = null) : ITelemetryRepository
{
    // All TelemetrySnapshot properties except identity/bookkeeping fields (Id, VehicleId, Vehicle,
    // RecordedAt, RawTopic) participate in field-by-field merging. Computed once and reused by both
    // MergeIntoAsync (last-write-wins) and GetMergedLatestAsync (first-non-null-wins) so a new
    // telemetry field only needs to be added to the model - not hand-copied into two merge loops.
    private static readonly HashSet<string> NonMergeableProperties =
    [
        nameof(TelemetrySnapshot.Id), nameof(TelemetrySnapshot.VehicleId),
        nameof(TelemetrySnapshot.Vehicle), nameof(TelemetrySnapshot.RecordedAt),
        nameof(TelemetrySnapshot.RawTopic),
    ];

    private static readonly PropertyInfo[] MergeableProperties = typeof(TelemetrySnapshot)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && !NonMergeableProperties.Contains(p.Name))
        .ToArray();

    // Daily counters reset at midnight - a stale value from a prior day must not be carried
    // forward into "today's" merged snapshot, so these two are merged with an extra date guard.
    private static readonly HashSet<string> DailyCounterFields =
    [
        nameof(TelemetrySnapshot.MileageOfTheDay), nameof(TelemetrySnapshot.PowerUsageOfDay),
    ];

    /// <summary>Overwrites every field on <paramref name="target"/> with the non-null value from <paramref name="source"/>, if any.</summary>
    private static void ApplyNonNullFields(TelemetrySnapshot target, TelemetrySnapshot source)
    {
        foreach (var prop in MergeableProperties)
        {
            var value = prop.GetValue(source);
            if (value is not null) prop.SetValue(target, value);
        }
    }

    /// <summary>Fills any still-empty field on <paramref name="target"/> from <paramref name="source"/>, leaving already-set fields untouched.</summary>
    private static void ApplyFirstNonNullFields(TelemetrySnapshot target, TelemetrySnapshot source, ISet<string>? skip = null)
    {
        foreach (var prop in MergeableProperties)
        {
            if (skip is not null && skip.Contains(prop.Name)) continue;
            if (prop.GetValue(target) is not null) continue;
            var value = prop.GetValue(source);
            if (value is not null) prop.SetValue(target, value);
        }
    }

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
             s.RearWindowDefroster != null || s.SteeringWheelHeating != null ||
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
        await NotifyUpdatedAsync(snapshot.VehicleId, ct);
        return snapshot.Id;
    }

    public async Task MergeIntoAsync(long rowId, TelemetrySnapshot patch, CancellationToken ct = default)
    {
        var existing = await db.TelemetrySnapshots.FindAsync([rowId], ct);
        if (existing is null)
        {
            db.TelemetrySnapshots.Add(patch);
            await db.SaveChangesAsync(ct);
            await NotifyUpdatedAsync(patch.VehicleId, ct);
            return;
        }

        // Last-write-wins per field: overwrite with any non-null value from the patch.
        ApplyNonNullFields(existing, patch);

        await db.SaveChangesAsync(ct);
        await NotifyUpdatedAsync(existing.VehicleId, ct);
    }

    private async Task NotifyUpdatedAsync(int vehicleId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_notify('telemetry_updated', {vehicleId.ToString()})", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The telemetry row itself is already saved at this point, so this only means
            // live SignalR/dashboard updates are delayed until the next poll, not data loss.
            logger?.LogWarning(ex, "Failed to notify telemetry_updated for vehicleId={VehicleId}", vehicleId);
        }
    }

    public Task<TelemetrySnapshot?> GetLatestAsync(int vehicleId, CancellationToken ct = default) =>
        db.TelemetrySnapshots
          .AsNoTracking()
          .Where(s => s.VehicleId == vehicleId)
          .OrderByDescending(s => s.RecordedAt)
          .FirstOrDefaultAsync(ct);

    public async Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var rows = await db.TelemetrySnapshots
            .AsNoTracking()
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
            ApplyFirstNonNullFields(merged, row, skip: DailyCounterFields);

            // Daily counters are only meaningful from today - don't carry yesterday's values forward
            if (row.RecordedAt >= todayStart)
            {
                merged.MileageOfTheDay ??= row.MileageOfTheDay;
                merged.PowerUsageOfDay ??= row.PowerUsageOfDay;
            }
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
                           GeoHelper.Haversine(prevLat.Value, prevLon!.Value,
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
            distance += GeoHelper.Haversine(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);

        // Reject GPS-teleportation: consecutive points implying > 250 km/h are not real trips
        for (var i = 1; i < points.Count; i++)
        {
            var segKm = GeoHelper.Haversine(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
            var segHours = (points[i].RecordedAt - points[i - 1].RecordedAt).TotalHours;
            if (segHours > 0 && segKm / segHours > 250)
                return null;
        }

        return new TripDto(index, points[0].RecordedAt, points[^1].RecordedAt, Math.Round(distance, 2), points.Count, points);
    }

    public async Task<VehicleAggregateStats> GetAggregateStatsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var climateKnown = await db.TelemetrySnapshots
            .Where(s => s.VehicleId == vehicleId && s.RecordedAt >= from && s.RecordedAt <= to && s.ClimateOn != null)
            .CountAsync(ct);

        var climateOn = climateKnown > 0
            ? await db.TelemetrySnapshots
                .Where(s => s.VehicleId == vehicleId && s.RecordedAt >= from && s.RecordedAt <= to && s.ClimateOn == true)
                .CountAsync(ct)
            : 0;

        return new VehicleAggregateStats(
            ClimateUsagePct: climateKnown > 0 ? (int)Math.Round((double)climateOn / climateKnown * 100) : null,
            ClimateOnSnapshots: climateOn,
            TotalClimateSnapshots: climateKnown
        );
    }
}
