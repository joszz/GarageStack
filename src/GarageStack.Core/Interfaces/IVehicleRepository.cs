using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>Looks up and creates <see cref="Vehicle"/> records by VIN, and updates their capability metadata.</summary>
public interface IVehicleRepository
{
    /// <summary>Every known vehicle, in the order they were first seen.</summary>
    Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken ct = default);

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

    /// <summary>
    /// Records when the vehicle was last parked, so the "parked recently" grace period survives
    /// a Worker restart.
    /// </summary>
    Task SetLastParkedAtAsync(int vehicleId, DateTime parkedAt, CancellationToken ct = default);

    /// <summary>
    /// Records the newest MG app message dealt with, so it is not pushed again when the gateway
    /// repeats it after a restart. See <see cref="Vehicle.LastMessageId"/>.
    /// </summary>
    Task SetLastMessageIdAsync(int vehicleId, string messageId, CancellationToken ct = default);
}
