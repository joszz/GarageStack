using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// When a trip is a commute: it runs between home and a work place, either way round. Only the
/// two ends count, so a stop on the way (home, bakery, work) makes two trips, neither a commute.
/// </summary>
public static class CommuteRule
{
    public static bool IsCommute(
        double startLatitude, double startLongitude, double endLatitude, double endLongitude,
        IReadOnlyList<CommutePlace> places)
    {
        bool At(CommutePlaceKind kind, double latitude, double longitude) =>
            places.Any(p => p.Kind == kind && IsWithin(p, latitude, longitude));

        return (At(CommutePlaceKind.Home, startLatitude, startLongitude) && At(CommutePlaceKind.Work, endLatitude, endLongitude))
            || (At(CommutePlaceKind.Work, startLatitude, startLongitude) && At(CommutePlaceKind.Home, endLatitude, endLongitude));
    }

    /// <summary>The purpose an automatic label gives a trip: commute, or none at all.</summary>
    public static TripPurpose? AutomaticPurpose(
        double startLatitude, double startLongitude, double endLatitude, double endLongitude,
        IReadOnlyList<CommutePlace> places) =>
        IsCommute(startLatitude, startLongitude, endLatitude, endLongitude, places) ? TripPurpose.Commute : null;

    public static bool IsWithin(CommutePlace place, double latitude, double longitude) =>
        GeoHelper.Haversine(place.Latitude, place.Longitude, latitude, longitude) * 1000 <= place.RadiusMeters;
}
