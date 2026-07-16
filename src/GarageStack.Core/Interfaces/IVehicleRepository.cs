using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>Looks up and creates <see cref="Vehicle"/> records by VIN, and updates their capability metadata.</summary>
public interface IVehicleRepository
{
    Task<Vehicle?> GetByVinAsync(string vin, CancellationToken ct = default);

    /// <summary>
    /// Returns the existing vehicle for <paramref name="vin"/>, or creates one if this is the
    /// first time it's been seen. The Api and Worker can both call this for a brand-new VIN at
    /// nearly the same time (e.g. on startup); implementations must handle the resulting
    /// unique-constraint race rather than let it surface as an error.
    /// </summary>
    Task<Vehicle> GetOrCreateByVinAsync(string vin, string? saicUser = null, CancellationToken ct = default);

    /// <summary>
    /// Merges <paramref name="key"/>/<paramref name="value"/> into the vehicle's capability
    /// config (parsed from MQTT <c>info/configuration/*</c> messages), leaving other keys intact.
    /// </summary>
    Task SetConfigValueAsync(int vehicleId, string key, string value, CancellationToken ct = default);

    Task SetModelAsync(int vehicleId, string model, CancellationToken ct = default);
}
