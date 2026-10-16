using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// Stores and queries raw and merged vehicle telemetry. Each MQTT message from the SAIC
/// gateway carries only the field(s) for one topic, so a single "poll" of the vehicle
/// arrives as several narrow rows within a short window - <see cref="AddAsync"/> and
/// <see cref="MergeIntoAsync"/> exist so a caller (MqttConsumerService) can fold those rows
/// into one, while the Get* methods present a merged view back out for the API/dashboard.
/// </summary>
public interface ITelemetryRepository
{
    /// <summary>Inserts <paramref name="snapshot"/> as a new row and returns its generated id.</summary>
    Task<long> AddAsync(TelemetrySnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Overwrites every field on the row identified by <paramref name="rowId"/> with the
    /// corresponding non-null field from <paramref name="patch"/> (last-write-wins per field).
    /// Used to fold a follow-up MQTT message into the same row an earlier message in the same
    /// poll cycle already created.
    /// </summary>
    Task MergeIntoAsync(long rowId, TelemetrySnapshot patch, CancellationToken ct = default);

    /// <summary>
    /// Builds a single "current state" snapshot by merging the most recent rows for
    /// <paramref name="vehicleId"/> field-by-field (first non-null value per field wins,
    /// newest row first) - this is what the dashboard, widget endpoint, and SignalR live
    /// updates all read, since no single MQTT message carries every field at once.
    /// </summary>
    Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>
    /// One <see cref="DailyStat"/> per local day in <paramref name="zone"/>, from
    /// <paramref name="from"/> to <paramref name="to"/> (or <paramref name="today"/>, whichever comes
    /// first), worked out from every reading of those days.
    /// </summary>
    Task<IReadOnlyList<DailyStat>> GetDailyStatsAsync(
        int vehicleId, DateOnly from, DateOnly to, TimeZoneInfo zone, DateOnly today, CancellationToken ct = default);

    /// <summary>
    /// The GPS fixes recorded from <paramref name="from"/> up to (not including) <paramref name="to"/>,
    /// oldest first: every row carrying a position, with the speed when that row has one. The raw
    /// material <see cref="Helpers.TripSegmenter"/> cuts trips from.
    /// </summary>
    Task<IReadOnlyList<TripPoint>> GetGpsFixesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>When the vehicle's first GPS fix was recorded, or null when it has never reported a position.</summary>
    Task<DateTime?> GetFirstGpsFixAtAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>
    /// The odometer as last reported at or before <paramref name="at"/>, or null when the vehicle
    /// had not reported one by then. The odometer only counts up, so the latest reading is the
    /// reading at that moment.
    /// </summary>
    Task<double?> GetOdometerAtAsync(int vehicleId, DateTime at, CancellationToken ct = default);

    /// <summary>
    /// The car's last <paramref name="count"/> status polls (lock, doors, windows), newest first.
    /// A poll the broker delivers again, as it does with retained messages on a reconnect, is still
    /// one poll: it comes back once, at the time it first arrived.
    /// </summary>
    Task<IReadOnlyList<StatusReading>> GetStatusReadingsAsync(int vehicleId, int count, CancellationToken ct = default);

    /// <summary>
    /// Why the car stopped charging, as its newest charge poll says, or null when that poll gave no
    /// reason. Read from that one poll: the gateway sends a reason only while the car reports one,
    /// so the newest reason on record can belong to a charge that ended long ago.
    /// </summary>
    Task<string?> GetChargingStopReasonAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>
    /// The raw MQTT topics that started telemetry rows for <paramref name="vehicleId"/> since
    /// <paramref name="since"/>, most frequent first.
    /// </summary>
    Task<IReadOnlyList<RawTopicStat>> GetRawTopicStatsAsync(int vehicleId, DateTime since, CancellationToken ct = default);

    /// <summary>
    /// Every row recorded for <paramref name="vehicleId"/>, oldest first, one at a time: for the data
    /// export, which can run to hundreds of thousands of rows.
    /// </summary>
    IAsyncEnumerable<TelemetrySnapshot> StreamAllAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>Computes summary statistics (e.g. climate usage) over snapshots in the given date range.</summary>
    Task<VehicleAggregateStats> GetAggregateStatsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);
}
