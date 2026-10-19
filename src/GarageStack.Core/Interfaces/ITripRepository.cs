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
    /// The same trips as <see cref="GetTripsAsync"/>, without their fixes. Saved trips are read
    /// from their stored figures, so a long period costs a row per trip rather than its GPS history.
    /// </summary>
    Task<IReadOnlyList<TripSummaryDto>> GetSummariesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Works out the speed figures of up to <paramref name="batchSize"/> saved trips that have none
    /// yet, trips saved before they were stored. Returns how many it did; fewer than the batch means
    /// none are left.
    /// </summary>
    Task<int> FillSpeedFiguresAsync(int batchSize, CancellationToken ct = default);

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
    /// Records what a trip was for and any notes, replacing what was there. A purpose that changes
    /// counts as chosen by hand, and is no longer labelled automatically. Returns the updated
    /// entry, or null when the vehicle has no such trip.
    /// </summary>
    Task<TripLogEntry?> SetPurposeAndNotesAsync(int vehicleId, long id, TripPurpose? purpose, string? notes, CancellationToken ct = default);

    /// <summary>
    /// Sets the purpose of every trip in <paramref name="ids"/> that belongs to the vehicle, by
    /// hand, leaving its notes alone. Returns how many trips were changed.
    /// </summary>
    Task<int> SetPurposeAsync(int vehicleId, IReadOnlyCollection<long> ids, TripPurpose? purpose, CancellationToken ct = default);

    /// <summary>
    /// Labels every trip nobody chose a purpose for against <paramref name="places"/>: a commute
    /// when it runs between home and work, unlabelled otherwise. Trips someone chose for are left
    /// alone, so moving home or work re-labels only what was labelled automatically.
    /// </summary>
    Task<CommuteRelabel> RelabelCommutesAsync(int vehicleId, IReadOnlyList<CommutePlace> places, CancellationToken ct = default);

    /// <summary>
    /// Hides a saved trip from the map, the statistics, the dashboard and the trip log's totals,
    /// or shows it again. Returns the updated entry, or null when the vehicle has no such trip.
    /// </summary>
    Task<TripLogEntry?> SetHiddenAsync(int vehicleId, long id, bool hidden, CancellationToken ct = default);

    /// <summary>
    /// Joins a saved trip with the saved trip after it, into one from the first one's start to the
    /// second one's end: two trips a short stop (a fuel station, a drop-off) split. The joined trip
    /// keeps the first one's id; the second is removed. A purpose someone chose is kept, the first
    /// trip's before the second's; otherwise it is labelled again from its new ends, so home to a
    /// fuel station to work becomes a commute.
    /// </summary>
    Task<TripMergeResult> MergeWithNextAsync(int vehicleId, long id, CancellationToken ct = default);

    /// <summary>
    /// Keeps the places now known for these trips. A null place leaves what is stored alone, so a
    /// caller can pass only what it has just resolved.
    /// </summary>
    Task SavePlacesAsync(int vehicleId, IReadOnlyList<TripPlaces> places, CancellationToken ct = default);
}
