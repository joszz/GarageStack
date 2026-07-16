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
    /// Returns chart-relevant snapshots between <paramref name="from"/> and <paramref name="to"/>
    /// (GPS-only rows are excluded - see the Statistics/Map trip endpoints for route data),
    /// downsampled per-day to a resolution appropriate for the requested range's length.
    /// </summary>
    Task<IReadOnlyList<TelemetrySnapshot>> GetHistoryAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Reconstructs discrete trips from GPS rows between <paramref name="from"/> and
    /// <paramref name="to"/>, splitting on data gaps and sustained parking periods.
    /// </summary>
    Task<IReadOnlyList<TripDto>> GetTripsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Computes summary statistics (e.g. climate usage) over snapshots in the given date range.</summary>
    Task<VehicleAggregateStats> GetAggregateStatsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);
}
