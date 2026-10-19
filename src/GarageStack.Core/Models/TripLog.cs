namespace GarageStack.Core.Models;

/// <summary>A finished trip with the odometer readings at either end, as the Worker saves it.</summary>
public sealed record RecordedTrip(TripDto Trip, double? OdometerStartKm, double? OdometerEndKm);

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
    bool IsHidden = false);

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
}

/// <summary>The joined trip as the trip log shows it, when <see cref="Outcome"/> is <see cref="TripMergeOutcome.Merged"/>.</summary>
public sealed record TripMergeResult(TripMergeOutcome Outcome, TripLogEntry? Merged = null);

/// <summary>How many trips a re-label marked as commutes, and how many it unmarked.</summary>
public sealed record CommuteRelabel(int Marked, int Cleared);

/// <summary>The places now known for one trip. Either may still be null when it is not resolved yet.</summary>
public sealed record TripPlaces(long Id, PlaceAddress? StartPlace, PlaceAddress? EndPlace);
