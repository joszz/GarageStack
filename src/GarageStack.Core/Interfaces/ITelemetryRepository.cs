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

    /// <summary>Returns the single most recently recorded row for <paramref name="vehicleId"/>, unmerged.</summary>
    Task<TelemetrySnapshot?> GetLatestAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>
    /// Builds a single "current state" snapshot by merging the most recent rows for
    /// <paramref name="vehicleId"/> field-by-field (first non-null value per field wins,
    /// newest row first) - this is what the dashboard, widget endpoint, and SignalR live
    /// updates all read, since no single MQTT message carries every field at once.
    /// </summary>
    Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>
    /// Returns the chart-relevant fields of snapshots between <paramref name="from"/> and
    /// <paramref name="to"/> (GPS-only rows are excluded - see the Statistics/Map trip endpoints
    /// for route data), downsampled per-day to a resolution appropriate for the requested range.
    /// </summary>
    Task<IReadOnlyList<TelemetryHistoryPoint>> GetHistoryAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// The GPS fixes recorded from <paramref name="from"/> up to (not including) <paramref name="to"/>,
    /// oldest first: every row carrying a position, with the speed when that row has one. The raw
    /// material <see cref="Helpers.TripSegmenter"/> cuts trips from.
    /// </summary>
    Task<IReadOnlyList<TripPoint>> GetGpsFixesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>When the vehicle's first GPS fix was recorded, or null when it has never reported a position.</summary>
    Task<DateTime?> GetFirstGpsFixAtAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>Distance and timestamp of the newest row that reported a journey in progress, or null when none exists.</summary>
    Task<LastTripSummary?> GetLastTripSummaryAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>The raw MQTT topics that started telemetry rows for <paramref name="vehicleId"/>, most frequent first.</summary>
    Task<IReadOnlyList<RawTopicStat>> GetRawTopicStatsAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>Computes summary statistics (e.g. climate usage) over snapshots in the given date range.</summary>
    Task<VehicleAggregateStats> GetAggregateStatsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);
}
