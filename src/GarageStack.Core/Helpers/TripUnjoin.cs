using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Undoing a join (<see cref="TripJoin"/>): what both trip repositories decide alike about the two
/// trips they put back.
/// </summary>
public static class TripUnjoin
{
    /// <summary>
    /// The purpose a trip put back gets: the one someone chose stays, otherwise it is worked out
    /// again from home and work as they are now, since they may have moved since the join.
    /// </summary>
    public static TripPurpose? Purpose(
        TripPurpose? purpose, bool purposeIsManual,
        double startLatitude, double startLongitude, double endLatitude, double endLongitude,
        IReadOnlyList<CommutePlace> places) =>
        purposeIsManual
            ? purpose
            : CommuteRule.AutomaticPurpose(startLatitude, startLongitude, endLatitude, endLongitude, places);
}
