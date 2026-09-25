using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// A vehicle's trips: the finished ones the Worker has saved, and the newer fixes it has not cut
/// into saved trips yet. <see cref="Vehicle.TripsRecordedUntil"/> is the line between the two.
/// </summary>
public interface ITripRepository
{
    /// <summary>
    /// Trips starting between <paramref name="from"/> and <paramref name="to"/>, oldest first: the
    /// saved ones, followed by the trips cut live from the fixes past the recording line, which
    /// includes the trip being driven.
    /// </summary>
    Task<IReadOnlyList<TripDto>> GetTripsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>The vehicle's <see cref="Vehicle.TripsRecordedUntil"/>, or null when nothing has been recorded.</summary>
    Task<DateTime?> GetRecordedUntilAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>
    /// Saves <paramref name="trips"/> and moves the recording line to <paramref name="recordedUntil"/>
    /// in one transaction, so a trip is never saved without the line passing it, or the other way round.
    /// </summary>
    Task SaveRecordedAsync(int vehicleId, IReadOnlyList<TripDto> trips, DateTime recordedUntil, CancellationToken ct = default);
}
