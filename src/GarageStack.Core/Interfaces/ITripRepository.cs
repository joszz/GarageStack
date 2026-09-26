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

    /// <summary>
    /// The vehicle's newest trip at <paramref name="now"/>: the one being driven or finished but not
    /// saved yet when there is one, otherwise the newest saved trip. Null when it has none.
    /// </summary>
    Task<TripDto?> GetLatestAsync(int vehicleId, DateTime now, CancellationToken ct = default);

    /// <summary>The vehicle's <see cref="Vehicle.TripsRecordedUntil"/>, or null when nothing has been recorded.</summary>
    Task<DateTime?> GetRecordedUntilAsync(int vehicleId, CancellationToken ct = default);

    /// <summary>
    /// Saves <paramref name="trips"/> and moves the recording line to <paramref name="recordedUntil"/>
    /// in one transaction, so a trip is never saved without the line passing it, or the other way round.
    /// </summary>
    Task SaveRecordedAsync(int vehicleId, IReadOnlyList<RecordedTrip> trips, DateTime recordedUntil, CancellationToken ct = default);

    /// <summary>
    /// The trip log: saved trips starting from <paramref name="from"/> up to (not including)
    /// <paramref name="to"/>, oldest first. Only saved trips, since only they can be annotated.
    /// </summary>
    Task<IReadOnlyList<TripLogEntry>> GetLogAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>The log entries for <paramref name="ids"/> that belong to the vehicle, in no particular order.</summary>
    Task<IReadOnlyList<TripLogEntry>> GetLogEntriesAsync(int vehicleId, IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>
    /// Records what a trip was for and any notes, replacing what was there. Returns the updated
    /// entry, or null when the vehicle has no such trip.
    /// </summary>
    Task<TripLogEntry?> SetPurposeAndNotesAsync(int vehicleId, long id, TripPurpose? purpose, string? notes, CancellationToken ct = default);

    /// <summary>
    /// Sets the purpose of every trip in <paramref name="ids"/> that belongs to the vehicle, leaving
    /// its notes alone. Returns how many trips were changed.
    /// </summary>
    Task<int> SetPurposeAsync(int vehicleId, IReadOnlyCollection<long> ids, TripPurpose? purpose, CancellationToken ct = default);

    /// <summary>
    /// Keeps the places now known for these trips. A null place leaves what is stored alone, so a
    /// caller can pass only what it has just resolved.
    /// </summary>
    Task SavePlacesAsync(int vehicleId, IReadOnlyList<TripPlaces> places, CancellationToken ct = default);
}
