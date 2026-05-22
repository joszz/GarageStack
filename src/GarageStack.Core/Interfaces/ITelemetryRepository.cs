using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

public interface ITelemetryRepository
{
    Task AddAsync(TelemetrySnapshot snapshot, CancellationToken ct = default);
    Task<TelemetrySnapshot?> GetLatestAsync(int vehicleId, CancellationToken ct = default);
    Task<TelemetrySnapshot?> GetMergedLatestAsync(int vehicleId, CancellationToken ct = default);
    Task<IReadOnlyList<TelemetrySnapshot>> GetHistoryAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<TripDto>> GetTripsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);
}
