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
    string? Notes);

/// <summary>The places now known for one trip. Either may still be null when it is not resolved yet.</summary>
public sealed record TripPlaces(long Id, PlaceAddress? StartPlace, PlaceAddress? EndPlace);
