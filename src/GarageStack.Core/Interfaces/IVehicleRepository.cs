using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

public interface IVehicleRepository
{
    Task<Vehicle?> GetByVinAsync(string vin, CancellationToken ct = default);
    Task<Vehicle> GetOrCreateByVinAsync(string vin, string? saicUser = null, CancellationToken ct = default);
    Task SetConfigValueAsync(int vehicleId, string key, string value, CancellationToken ct = default);
    Task SetModelAsync(int vehicleId, string model, CancellationToken ct = default);
}
