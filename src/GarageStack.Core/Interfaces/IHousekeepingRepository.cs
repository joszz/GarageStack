using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// Keeps the database from growing without end: folds old telemetry into quarter-hour rows, and
/// removes what nothing reads any more.
/// </summary>
public interface IHousekeepingRepository
{
    /// <summary>When the vehicle's oldest telemetry row was recorded, or null when it has none.</summary>
    Task<DateTime?> GetFirstTelemetryAtAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>
    /// Compacts the vehicle's telemetry from <paramref name="from"/> up to (not including)
    /// <paramref name="until"/> and moves <see cref="Vehicle.TelemetryCompactedUntil"/> to
    /// <paramref name="until"/>, in one transaction.
    /// </summary>
    /// <returns>How many rows were folded into others and removed.</returns>
    Task<int> CompactTelemetryAsync(int vehicleId, DateTime from, DateTime until, CancellationToken ct = default);

    /// <summary>Removes the place names and snapped trips that expired before <paramref name="before"/>.</summary>
    /// <returns>How many cache entries were removed.</returns>
    Task<int> RemoveExpiredCacheEntriesAsync(DateTime before, CancellationToken ct = default);

    /// <summary>Removes the notifications the user deleted that were created before <paramref name="createdBefore"/>.</summary>
    /// <returns>How many notifications were removed.</returns>
    Task<int> RemoveDeletedNotificationsAsync(DateTime createdBefore, CancellationToken ct = default);
}
