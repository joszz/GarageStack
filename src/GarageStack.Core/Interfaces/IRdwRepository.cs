using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>The RDW lookup kept per vehicle (<see cref="RdwVehicleInfo"/>).</summary>
public interface IRdwRepository
{
    Task<RdwVehicleInfo?> GetAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>Every vehicle's row, for the Worker's round.</summary>
    Task<IReadOnlyList<RdwVehicleInfo>> ListAsync(CancellationToken ct = default);

    /// <summary>Creates or replaces the vehicle's row with <paramref name="info"/>'s values.</summary>
    Task SaveAsync(RdwVehicleInfo info, CancellationToken ct = default);

    /// <summary>Records that a lookup failed, leaving what the last good one found in place.</summary>
    Task SetErrorAsync(int vehicleId, string licensePlate, string error, CancellationToken ct = default);

    /// <summary>Marks the recalls with these codes as pushed.</summary>
    Task MarkRecallsNotifiedAsync(int vehicleId, IReadOnlyCollection<string> codes, DateTime notifiedAt, CancellationToken ct = default);

    /// <summary>Forgets the vehicle's lookup, when its plate is cleared or changed.</summary>
    Task DeleteAsync(int vehicleId, CancellationToken ct = default);
}
