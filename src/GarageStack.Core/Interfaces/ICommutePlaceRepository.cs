using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>A vehicle's home and work places, which label trips between them as commutes.</summary>
public interface ICommutePlaceRepository
{
    /// <summary>The vehicle's places, home first, then in the order they were added.</summary>
    Task<IReadOnlyList<CommutePlace>> ListAsync(int vehicleId, CancellationToken ct = default);

    Task<int> CountAsync(int vehicleId, CancellationToken ct = default);

    Task<CommutePlace> AddAsync(CommutePlace place, CancellationToken ct = default);

    /// <summary>Applies <paramref name="change"/> to the place and saves it; null when the vehicle has no such place.</summary>
    Task<CommutePlace?> UpdateAsync(int vehicleId, int id, Action<CommutePlace> change, CancellationToken ct = default);

    Task<bool> DeleteAsync(int vehicleId, int id, CancellationToken ct = default);
}
