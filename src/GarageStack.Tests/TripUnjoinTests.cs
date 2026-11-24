using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TripUnjoinTests
{
    private static readonly CommutePlace[] Places =
    [
        new() { Kind = CommutePlaceKind.Home, Latitude = 52.0, Longitude = 5.0 },
        new() { Kind = CommutePlaceKind.Work, Latitude = 52.2, Longitude = 5.0 },
    ];

    [Fact]
    public void APurposeSomeoneChose_StaysAsItWas()
    {
        Assert.Equal(TripPurpose.Private, TripUnjoin.Purpose(TripPurpose.Private, true, 52.0, 5.0, 52.2, 5.0, Places));
        Assert.Null(TripUnjoin.Purpose(null, true, 52.0, 5.0, 52.2, 5.0, Places));
    }

    [Fact]
    public void APurposeNobodyChose_FollowsHomeAndWorkAsTheyAreNow()
    {
        Assert.Equal(TripPurpose.Commute, TripUnjoin.Purpose(null, false, 52.0, 5.0, 52.2, 5.0, Places));
        Assert.Null(TripUnjoin.Purpose(TripPurpose.Commute, false, 52.0, 5.0, 52.2, 5.0, []));
    }
}
