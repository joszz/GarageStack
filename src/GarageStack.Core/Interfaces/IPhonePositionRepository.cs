using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>The positions the installed app sends from a phone in the car.</summary>
public interface IPhonePositionRepository
{
    /// <summary>
    /// Stores the positions a tracker does not have yet, so a batch sent again after a lost answer
    /// is stored once. A trip they fall in that found no phone before is queued to be fused again.
    /// </summary>
    /// <returns>How many positions were new.</returns>
    Task<int> AddAsync(int vehicleId, string trackerId, IReadOnlyList<PhonePosition> positions, CancellationToken ct);

    /// <summary>The app's positions for the vehicle between the two moments, as phone fixes.</summary>
    Task<IReadOnlyList<PhoneFix>> GetFixesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct);

    /// <summary>Every position the vehicle's trackers sent, oldest first, for the data export.</summary>
    Task<IReadOnlyList<PhonePosition>> ListAsync(int vehicleId, CancellationToken ct);

    /// <summary>Removes the positions recorded before <paramref name="before"/>.</summary>
    Task<int> RemoveOlderThanAsync(DateTime before, CancellationToken ct);
}
