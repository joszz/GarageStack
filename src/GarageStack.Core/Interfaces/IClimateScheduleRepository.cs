using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// Climate schedules, written by both processes: the Api saves what the user sets, the Worker
/// claims due runs and records how they went. Every write goes through here, so the two cannot
/// overwrite each other unseen (see <see cref="ClimateSchedule.Version"/>).
/// </summary>
public interface IClimateScheduleRepository
{
    /// <summary>A vehicle's schedules, earliest time of day first.</summary>
    Task<IReadOnlyList<ClimateSchedule>> ListAsync(int vehicleId, CancellationToken ct = default);

    Task<int> CountAsync(int vehicleId, CancellationToken ct = default);

    Task<ClimateSchedule> AddAsync(ClimateSchedule schedule, CancellationToken ct = default);

    /// <summary>
    /// Applies <paramref name="change"/> to the schedule and saves it. When the other process saved
    /// the schedule first, applies it again to what that save left. Null when there is no such schedule.
    /// </summary>
    Task<ClimateSchedule?> UpdateAsync(int vehicleId, int id, Action<ClimateSchedule> change, CancellationToken ct = default);

    /// <returns>false when there is no such schedule.</returns>
    Task<bool> DeleteAsync(int vehicleId, int id, CancellationToken ct = default);

    /// <summary>The switched-on schedules whose next run is at or before <paramref name="nowUtc"/>, with their vehicle.</summary>
    Task<IReadOnlyList<ClimateSchedule>> GetDueAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Applies <paramref name="claim"/> to <paramref name="due"/> (as <see cref="GetDueAsync"/> read
    /// it) and saves, unless the schedule was saved since. False then, and the run is left alone: the
    /// next look at what is due reads the schedule as it is now.
    /// </summary>
    Task<bool> TryClaimAsync(ClimateSchedule due, Action<ClimateSchedule> claim, CancellationToken ct = default);

    /// <summary>
    /// Turns every run still marked running into an unconfirmed one: called once at Worker start,
    /// after a restart cut those runs short.
    /// </summary>
    Task MarkInterruptedAsync(CancellationToken ct = default);
}
