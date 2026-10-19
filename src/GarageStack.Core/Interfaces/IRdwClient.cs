using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// The RDW's open data, asked by plate. Every call throws <see cref="RdwUnavailableException"/> when
/// the RDW could not be asked (switched off, rate-limited, unreachable) or answered with something
/// other than data.
/// </summary>
public interface IRdwClient
{
    /// <returns>The registration, or null when the RDW has no vehicle under <paramref name="plate"/>.</returns>
    Task<RdwRegistration?> GetRegistrationAsync(string plate, CancellationToken ct = default);

    /// <summary>Every recall the RDW lists for the plate, open or repaired.</summary>
    Task<IReadOnlyList<RdwRecallStatus>> GetRecallStatusesAsync(string plate, CancellationToken ct = default);

    /// <summary>What the recalls with these reference codes are about. A code the RDW does not know is left out.</summary>
    Task<IReadOnlyList<RdwRecallDetails>> GetRecallDetailsAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default);
}
