using System.Linq.Expressions;
using System.Reflection;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Repositories;

// logger/cache are optional (DI always supplies both) so existing tests can keep
// constructing this directly with just a DbContext.
public class TelemetryRepository(
    AppDbContext db,
    ILogger<TelemetryRepository>? logger = null,
    IMemoryCache? cache = null) : ITelemetryRepository
{
    // GetMergedLatestAsync scans up to 200 rows (plus up to 2 fallback queries) and runs on
    // every /status, /widget/status, maintenance-mutation, and SignalR-broadcast call. A write
    // (AddAsync/MergeIntoAsync) always invalidates the entry immediately via NotifyUpdatedAsync,
    // so this short TTL only bounds staleness for the rare case nothing has written new
    // telemetry at all - its real job is absorbing bursts of reads between writes (multiple
    // browser tabs, homepage widget polling, etc).
    private static readonly TimeSpan MergedLatestCacheTtl = TimeSpan.FromSeconds(5);

    private static string LatestCacheKey(int vehicleId) => $"telemetry-latest/{vehicleId}";

    // Defensive cap on raw row counts for GetGpsFixesAsync/GetHistoryAsync. Endpoints already cap
    // the requestable window to 90 days, the trip recorder reads a week at a time, and GPS rows
    // arrive roughly once/minute while driving, so this is far above any realistic heavy-user
    // volume - it exists purely to bound worst-case memory rather than to affect normal queries.
    private const int MaxRawRowsPerQuery = 200_000;

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

    // Compiled expression-tree accessors instead of live reflection (PropertyInfo.GetValue/
    // SetValue): this runs on every MQTT message merge, multiple times per second per vehicle,
    // and reflection's per-call overhead is avoidable since the property set is fixed at
    // startup. Built once here, then invoked like a regular delegate call from then on.
    private sealed record PropertyAccessor(string Name, Func<TelemetrySnapshot, object?> Get, Action<TelemetrySnapshot, object?> Set);

    private static PropertyAccessor BuildAccessor(PropertyInfo prop)
    {
        var instance = Expression.Parameter(typeof(TelemetrySnapshot), "instance");
        var value = Expression.Parameter(typeof(object), "value");

        var getter = Expression.Lambda<Func<TelemetrySnapshot, object?>>(
            Expression.Convert(Expression.Property(instance, prop), typeof(object)),
            instance).Compile();

        var setter = Expression.Lambda<Action<TelemetrySnapshot, object?>>(
            Expression.Assign(
                Expression.Property(instance, prop),
                Expression.Convert(value, prop.PropertyType)),
            instance, value).Compile();

        return new PropertyAccessor(prop.Name, getter, setter);
    }

    private static readonly PropertyInfo[] MergeablePropertyInfos = typeof(TelemetrySnapshot)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && !NonMergeableProperties.Contains(p.Name))
        .ToArray();

    private static readonly PropertyAccessor[] MergeableProperties =
        [.. MergeablePropertyInfos.Select(BuildAccessor)];

    /// <summary>
    /// Builds "any of these fields is set" as an expression tree EF can translate to SQL.
    /// Non-nullable properties are left out: they always have a value, so they say nothing about
    /// whether a row carries telemetry.
    /// </summary>
    private static Expression<Func<TelemetrySnapshot, bool>> AnyFieldSet(IEnumerable<PropertyInfo> properties)
    {
        var snapshot = Expression.Parameter(typeof(TelemetrySnapshot), "s");
        var conditions = properties
            .Where(p => !p.PropertyType.IsValueType || Nullable.GetUnderlyingType(p.PropertyType) is not null)
            .Select(p => (Expression)Expression.NotEqual(
                Expression.Property(snapshot, p),
                Expression.Constant(null, p.PropertyType)));

        return Expression.Lambda<Func<TelemetrySnapshot, bool>>(
            conditions.Aggregate(Expression.OrElse), snapshot);
    }

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
            var value = prop.Get(source);
            if (value is not null) prop.Set(target, value);
        }
    }

    /// <summary>Fills any still-empty field on <paramref name="target"/> from <paramref name="source"/>, leaving already-set fields untouched.</summary>
    private static void ApplyFirstNonNullFields(TelemetrySnapshot target, TelemetrySnapshot source, ISet<string>? skip = null)
    {
        foreach (var prop in MergeableProperties)
        {
            if (skip is not null && skip.Contains(prop.Name)) continue;
            if (prop.Get(target) is not null) continue;
            var value = prop.Get(source);
            if (value is not null) prop.Set(target, value);
        }
    }

    // A row is worth reading when any mergeable field carries a value. Derived from the model for
    // the same reason the merge loops are: hand-listing seventy fields here means a new telemetry
    // field is silently treated as empty until someone remembers to add it.
    private static readonly Expression<Func<TelemetrySnapshot, bool>> HasData =
        AnyFieldSet(MergeablePropertyInfos);

    // Chart history excludes GPS-only rows: latitude/longitude arrive every minute during driving
    // and inflate the row count, causing the stride downsampler to skip the sparser fuel/EV/kWh
    // rows. GPS data for routes belongs to the trips endpoint, not chart history. The fields that
    // count are exactly the ones TelemetryHistoryPoint carries, read from that type so the two
    // cannot drift apart.
    private static readonly Expression<Func<TelemetrySnapshot, bool>> HasChartData =
        AnyFieldSet(typeof(TelemetryHistoryPoint)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => typeof(TelemetrySnapshot).GetProperty(p.Name)
                ?? throw new InvalidOperationException(
                    $"TelemetryHistoryPoint.{p.Name} has no matching TelemetrySnapshot property.")));

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
        // Always invalidate, relational or not - GetMergedLatestAsync's cache must never
        // outlive the write that just happened, since TelemetryNotificationService reads
        // through this same cache to build the "live" SignalR broadcast.
        cache?.Remove(LatestCacheKey(vehicleId));

        try
        {
            await db.Database.NotifyAsync(PgChannels.TelemetryUpdated, vehicleId.ToString(), ct);
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
        var cacheKey = LatestCacheKey(vehicleId);
        if (cache is not null && cache.TryGetValue(cacheKey, out TelemetrySnapshot? cached))
            return cached;

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
            var engineOff = merged.EngineRunning == false;
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
                    s.ChargingScheduleMode,
                    s.ChargingScheduleStartTime,
                    s.ChargingScheduleEndTime,
                    s.BatteryHeatingScheduleMode,
                    s.BatteryHeatingScheduleStartTime,
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

        cache?.Set(cacheKey, merged, MergedLatestCacheTtl);
        return merged;
    }

    public async Task<IReadOnlyList<TelemetryHistoryPoint>> GetHistoryAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        // Ordered newest-first with a cap, then reversed back to chronological order below:
        // if the cap is ever hit, it's the oldest rows in the range that get dropped, not the
        // newest - recent history matters more to users than the tail of a 90-day window.
        // Only the chart fields are selected: a full snapshot row is ~70 columns, almost all of
        // which the statistics page never reads.
        var rows = await db.TelemetrySnapshots
            .AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.RecordedAt >= from && s.RecordedAt <= to)
            .Where(HasChartData)
            .OrderByDescending(s => s.RecordedAt)
            .Take(MaxRawRowsPerQuery)
            .Select(TelemetryHistoryPoint.Projection)
            .ToListAsync(ct);

        if (rows.Count == MaxRawRowsPerQuery)
            logger?.LogWarning(
                "GetHistoryAsync hit the {Cap}-row cap for vehicleId={VehicleId} ({From} to {To}); oldest rows in range were dropped",
                MaxRawRowsPerQuery, vehicleId, from, to);

        rows.Reverse();

        if (rows.Count == 0) return rows;

        var maxPoints = (to - from).TotalDays switch
        {
            <= 1 => 288,  // ~5-min resolution
            <= 7 => 336,  // ~30-min resolution
            _ => 360,  // ~2-hour resolution
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
        var result = new List<TelemetryHistoryPoint>(maxPoints + dayGroups.Count);
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

    public async Task<IReadOnlyList<TripPoint>> GetGpsFixesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        // Includes speed=0 fixes: the trip splitter needs them to see the car parked between
        // back-to-back trips. Ordered newest-first with a cap, then reversed back to chronological
        // order below: if the cap is ever hit, it's the oldest rows in the range that get dropped,
        // not the newest - recent trips matter more to users than the tail of a 90-day window.
        var fixes = await db.TelemetrySnapshots
            .AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.RecordedAt >= from && s.RecordedAt < to
                        && s.Latitude != null && s.Longitude != null)
            .OrderByDescending(s => s.RecordedAt)
            .Select(s => new TripPoint(s.RecordedAt, s.Latitude!.Value, s.Longitude!.Value, s.Speed))
            .Take(MaxRawRowsPerQuery)
            .ToListAsync(ct);

        if (fixes.Count == MaxRawRowsPerQuery)
            logger?.LogWarning(
                "GetGpsFixesAsync hit the {Cap}-row cap for vehicleId={VehicleId} ({From} to {To}); oldest rows in range were dropped",
                MaxRawRowsPerQuery, vehicleId, from, to);

        fixes.Reverse();
        return fixes;
    }

    public Task<DateTime?> GetFirstGpsFixAtAsync(int vehicleId, CancellationToken ct = default) =>
        db.TelemetrySnapshots
            .AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.Latitude != null && s.Longitude != null)
            .OrderBy(s => s.RecordedAt)
            .Select(s => (DateTime?)s.RecordedAt)
            .FirstOrDefaultAsync(ct);

    public Task<LastTripSummary?> GetLastTripSummaryAsync(int vehicleId, CancellationToken ct = default) =>
        db.TelemetrySnapshots
            .AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.CurrentJourneyDistance > 0)
            .OrderByDescending(s => s.RecordedAt)
            .Select(s => new LastTripSummary(s.CurrentJourneyDistance!.Value, s.RecordedAt))
            .FirstOrDefaultAsync(ct);

    // Ordered on the group before projecting: EF cannot translate an OrderBy on a property of a
    // record built through its constructor, so sorting after the Select fails at runtime.
    public async Task<IReadOnlyList<RawTopicStat>> GetRawTopicStatsAsync(int vehicleId, CancellationToken ct = default) =>
        await db.TelemetrySnapshots
            .AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.RawTopic != null)
            .GroupBy(s => s.RawTopic!)
            .OrderByDescending(g => g.Count())
            .Select(g => new RawTopicStat(g.Key, g.Count(), g.Max(s => s.RecordedAt)))
            .ToListAsync(ct);

    public async Task<VehicleAggregateStats> GetAggregateStatsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        // Single grouped query instead of two round-trips: total count and the
        // ClimateOn == true count are both conditional aggregates over the same filtered set.
        var agg = await db.TelemetrySnapshots
            .Where(s => s.VehicleId == vehicleId && s.RecordedAt >= from && s.RecordedAt <= to && s.ClimateOn != null)
            .GroupBy(s => 1)
            .Select(g => new { Known = g.Count(), On = g.Count(s => s.ClimateOn == true) })
            .FirstOrDefaultAsync(ct);

        var climateKnown = agg?.Known ?? 0;
        var climateOn = agg?.On ?? 0;

        return new VehicleAggregateStats(
            ClimateUsagePct: climateKnown > 0 ? (int)Math.Round((double)climateOn / climateKnown * 100) : null,
            ClimateOnSnapshots: climateOn,
            TotalClimateSnapshots: climateKnown
        );
    }
}
