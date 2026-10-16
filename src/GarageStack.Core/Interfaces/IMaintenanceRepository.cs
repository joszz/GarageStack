using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// Maintenance items and their service history. An item's last service (its baseline for what is
/// due next) always follows the latest entry in its history, so the methods that change the
/// history move it too.
/// </summary>
public interface IMaintenanceRepository
{
    /// <summary>A vehicle's items, by name.</summary>
    Task<IReadOnlyList<MaintenanceItem>> ListAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>Every vehicle's items, for the Worker's due check.</summary>
    Task<IReadOnlyList<MaintenanceItem>> ListAllAsync(CancellationToken ct = default);

    Task<MaintenanceItem> AddAsync(MaintenanceItem item, CancellationToken ct = default);

    /// <summary>Applies <paramref name="change"/> to the item and saves it. Null when the vehicle has no such item.</summary>
    Task<MaintenanceItem?> UpdateAsync(int vehicleId, int id, Action<MaintenanceItem> change, CancellationToken ct = default);

    /// <summary>Deletes the item with its history.</summary>
    /// <returns>false when the vehicle has no such item.</returns>
    Task<bool> DeleteAsync(int vehicleId, int id, CancellationToken ct = default);

    /// <summary>The item's history, latest service first. Null when the vehicle has no such item.</summary>
    Task<IReadOnlyList<MaintenanceLogEntry>?> GetLogAsync(int vehicleId, int itemId, CancellationToken ct = default);

    /// <summary>Records a service against the item. Null when the vehicle has no such item.</summary>
    Task<MaintenanceServiceLogged?> LogServiceAsync(int vehicleId, int itemId, MaintenanceLogEntry entry, CancellationToken ct = default);

    /// <summary>Removes an entry from the item's history. Null when there is no such item or entry.</summary>
    Task<MaintenanceItem?> DeleteLogEntryAsync(int vehicleId, int itemId, int logId, CancellationToken ct = default);
}

/// <summary>A service just logged, and the item it moved forward.</summary>
public sealed record MaintenanceServiceLogged(MaintenanceItem Item, MaintenanceLogEntry Entry);
