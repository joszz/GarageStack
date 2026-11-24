namespace GarageStack.Core.Models;

/// <summary>
/// A finished trip with the odometer readings at either end, as the Worker saves it, and what the
/// day counter rose by over it (<see cref="Helpers.TripCounterUse"/>) from how many readings.
/// </summary>
public sealed record RecordedTrip(
    TripDto Trip, double? OdometerStartKm, double? OdometerEndKm, double? CounterUsed = null, int CounterReadings = 0);

/// <summary>
/// One line of the trip log: a saved trip without its fixes, plus what the driver recorded about
/// it. A place is null until it has been looked up.
/// </summary>
public sealed record TripLogEntry(
    long Id,
    DateTime StartedAt,
    DateTime EndedAt,
    double DistanceKm,
    double StartLatitude,
    double StartLongitude,
    double EndLatitude,
    double EndLongitude,
    double? OdometerStartKm,
    double? OdometerEndKm,
    PlaceAddress? StartPlace,
    PlaceAddress? EndPlace,
    TripPurpose? Purpose,
    string? Notes,
    // Set from home and work rather than by someone (Trip.PurposeIsManual).
    bool PurposeIsAutomatic = false,
    // Left out of everything but the trip log's list of hidden trips (Trip.IsHidden).
    bool IsHidden = false,
    // Its route was edited by hand (Trip.EditedAt).
    bool IsEdited = false,
    // The highest speed a fix reported, and the mean while moving; null when no fix carried one.
    double? MaxSpeedKmh = null,
    double? AvgMovingSpeedKmh = null,
    DateTime? PhoneFusionAt = null,
    // What the day counter rose by over the trip, in its own unit (Trip.CounterUsed).
    double? CounterUsed = null,
    // Joined from two trips, and the join can be undone (Trip.JoinedFromJson).
    bool IsJoined = false);

/// <summary>How joining a trip with the next one went (<see cref="Interfaces.ITripRepository.MergeWithNextAsync"/>).</summary>
public enum TripMergeOutcome
{
    Merged,

    /// <summary>The vehicle has no such saved trip.</summary>
    NotFound,

    /// <summary>No saved trip follows it yet.</summary>
    NoNextTrip,

    /// <summary>The fixes of one of the two cannot be read, so there is nothing to join.</summary>
    Unreadable,

    /// <summary>One of the two changed while joining them (the Worker added the phone's points); nothing was saved.</summary>
    Conflict,
}

/// <summary>The joined trip as the trip log shows it, when <see cref="Outcome"/> is <see cref="TripMergeOutcome.Merged"/>.</summary>
public sealed record TripMergeResult(TripMergeOutcome Outcome, TripLogEntry? Merged = null);

/// <summary>How undoing a join went (<see cref="Interfaces.ITripRepository.UnjoinAsync"/>).</summary>
public enum TripUnjoinOutcome
{
    Unjoined,

    /// <summary>The vehicle has no such saved trip.</summary>
    NotFound,

    /// <summary>The trip was not joined, or was joined before joins could be undone.</summary>
    NotJoined,

    /// <summary>What the trip was joined from cannot be read, so there is nothing to put back.</summary>
    Unreadable,

    /// <summary>
    /// Another trip now runs into the time the two would take again, after an edit since the join;
    /// nothing was changed.
    /// </summary>
    Overlaps,

    /// <summary>The trip changed while the join was being undone (the Worker added the phone's points); nothing was saved.</summary>
    Conflict,
}

/// <summary>The two trips put back, oldest first, when <see cref="Outcome"/> is <see cref="TripUnjoinOutcome.Unjoined"/>.</summary>
public sealed record TripUnjoinResult(TripUnjoinOutcome Outcome, IReadOnlyList<TripLogEntry>? Trips = null);

/// <summary>How many trips a re-label marked as commutes, and how many it unmarked.</summary>
public sealed record CommuteRelabel(int Marked, int Cleared);

/// <summary>The places now known for one trip. Either may still be null when it is not resolved yet.</summary>
public sealed record TripPlaces(long Id, PlaceAddress? StartPlace, PlaceAddress? EndPlace);
