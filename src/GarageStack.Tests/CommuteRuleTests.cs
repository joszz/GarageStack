using GarageStack.Api.Endpoints;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class CommuteRuleTests
{
    // About 111 m per thousandth of a degree of latitude.
    private const double HomeLat = 52.0000, WorkLat = 52.1000, ShopLat = 52.0500, Lon = 5.0;

    private static readonly CommutePlace Home = new() { Kind = CommutePlaceKind.Home, Latitude = HomeLat, Longitude = Lon };
    private static readonly CommutePlace Work = new() { Kind = CommutePlaceKind.Work, Latitude = WorkLat, Longitude = Lon };

    private static bool Commute(double fromLat, double toLat, params CommutePlace[] places) =>
        CommuteRule.IsCommute(fromLat, Lon, toLat, Lon, places);

    [Fact]
    public void HomeToWork_AndBack_AreCommutes()
    {
        Assert.True(Commute(HomeLat, WorkLat, Home, Work));
        Assert.True(Commute(WorkLat, HomeLat, Home, Work));
    }

    [Fact]
    public void ATripWithOnlyOneEndAtHomeOrWork_IsNoCommute()
    {
        Assert.False(Commute(HomeLat, ShopLat, Home, Work));
        Assert.False(Commute(ShopLat, WorkLat, Home, Work));
        Assert.False(Commute(HomeLat, HomeLat, Home, Work));
    }

    // A car parks down the street, or in the office garage.
    [Fact]
    public void AnEndWithinTheRadius_CountsAsThePlace()
    {
        var widerHome = new CommutePlace { Kind = CommutePlaceKind.Home, Latitude = HomeLat, Longitude = Lon, RadiusMeters = 400 };

        Assert.True(Commute(HomeLat + 0.002, WorkLat, Home, Work));
        Assert.False(Commute(HomeLat + 0.003, WorkLat, Home, Work));
        Assert.True(Commute(HomeLat + 0.003, WorkLat, widerHome, Work));
    }

    [Fact]
    public void AnyOfSeveralWorkPlaces_Counts()
    {
        var otherOffice = new CommutePlace { Kind = CommutePlaceKind.Work, Latitude = ShopLat, Longitude = Lon };

        Assert.True(Commute(HomeLat, ShopLat, Home, Work, otherOffice));
    }

    [Fact]
    public void WithoutAWorkPlace_NothingIsACommute()
    {
        Assert.False(Commute(HomeLat, WorkLat, Home));
    }
}

public class CommutePlaceValidationTests
{
    private static CommutePlaceRequest Valid() => new(CommutePlaceKind.Work, " Office ", 52.1, 5.0, 250);

    [Fact]
    public void AValidPlace_Passes()
    {
        Assert.Null(CommutePlaceEndpoints.Validate(Valid()));
        Assert.Null(CommutePlaceEndpoints.Validate(Valid() with { RadiusMeters = null, Label = null }));
    }

    [Theory]
    [InlineData(null, 52.1, 5.0, 250, "commutePlace.kindRequired")]
    [InlineData(CommutePlaceKind.Home, 91.0, 5.0, 250, "commutePlace.invalidPosition")]
    [InlineData(CommutePlaceKind.Home, 52.1, double.NaN, 250, "commutePlace.invalidPosition")]
    [InlineData(CommutePlaceKind.Home, null, 5.0, 250, "commutePlace.invalidPosition")]
    [InlineData(CommutePlaceKind.Home, 52.1, 5.0, 20, "commutePlace.invalidRadius")]
    [InlineData(CommutePlaceKind.Home, 52.1, 5.0, 5000, "commutePlace.invalidRadius")]
    public void AnInvalidPlace_IsRefusedWithItsCode(CommutePlaceKind? kind, double? lat, double? lon, int radius, string code)
    {
        Assert.Equal(code, CommutePlaceEndpoints.Validate(new CommutePlaceRequest(kind, null, lat, lon, radius))?.Code);
    }

    [Fact]
    public void ATooLongLabel_IsRefused()
    {
        var label = new string('x', CommutePlaceLimits.LabelMaxLength + 1);

        Assert.Equal("commutePlace.labelTooLong", CommutePlaceEndpoints.Validate(Valid() with { Label = label })?.Code);
    }
}
