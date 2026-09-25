using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

/// <summary>
/// Serves the demo's fixed trips. Demo mode runs no Worker, so nothing is ever recorded: saving is
/// a no-op and the recording line is never set.
/// </summary>
public sealed class DemoTripRepository : ITripRepository
{
    public Task<IReadOnlyList<TripDto>> GetTripsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TripDto>>(
            [.. DemoTrips.All
                .Where(t => t.StartedAt >= from && t.StartedAt < to)
                .Select((t, i) => t with { Index = i })]);

    public Task<DateTime?> GetRecordedUntilAsync(int vehicleId, CancellationToken ct = default) =>
        Task.FromResult<DateTime?>(null);

    public Task SaveRecordedAsync(int vehicleId, IReadOnlyList<TripDto> trips, DateTime recordedUntil, CancellationToken ct = default) =>
        Task.CompletedTask;
}
