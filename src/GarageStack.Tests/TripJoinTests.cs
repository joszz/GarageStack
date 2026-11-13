using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TripJoinTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    private static TripJoinPart Part(double fromLat, double toLat, int startMinute,
        TripPurpose? purpose = null, bool manual = false, string? notes = null, bool hidden = false, bool edited = false) => new(
        DistanceKm: 10, StartLatitude: fromLat, StartLongitude: 5, EndLatitude: toLat, EndLongitude: 5,
        Points: [new(T0.AddMinutes(startMinute), fromLat, 5, 40), new(T0.AddMinutes(startMinute + 10), toLat, 5, 60)],
        OriginalPoints: edited ? [new(T0.AddMinutes(startMinute), fromLat, 5, 40), new(T0.AddMinutes(startMinute + 10), toLat, 5, 60)] : null,
        IsHidden: hidden, Notes: notes, Purpose: purpose, PurposeIsManual: manual,
        PhoneFusion: null, PhoneFusionAt: null, EditedAt: edited ? T0.AddDays(1) : null);

    [Fact]
    public void TheDistance_IsBothTripsAndTheHopAcrossTheStop()
    {
        var joined = TripJoin.Join(Part(52.0, 52.1, 0), Part(52.11, 52.2, 20), []);

        Assert.Equal(20 + GeoHelper.Haversine(52.1, 5, 52.11, 5), joined.DistanceKm, 9);
        Assert.Equal(4, joined.Points.Count);
    }

    [Fact]
    public void APurposeChosenByHand_Wins_TheFirstTripsBeforeTheSeconds()
    {
        Assert.Equal(TripPurpose.Private, TripJoin.Join(
            Part(52.0, 52.1, 0, TripPurpose.Private, manual: true), Part(52.1, 52.2, 20, TripPurpose.Business, manual: true), []).Purpose);
        var second = TripJoin.Join(Part(52.0, 52.1, 0, TripPurpose.Commute), Part(52.1, 52.2, 20, TripPurpose.Business, manual: true), []);
        Assert.Equal((TripPurpose.Business, true), (second.Purpose, second.PurposeIsManual));
    }

    [Fact]
    public void WithoutAChosenPurpose_HomeAndWorkDecideForTheJoinedTrip()
    {
        CommutePlace[] places =
        [
            new() { Kind = CommutePlaceKind.Home, Latitude = 52.0, Longitude = 5 },
            new() { Kind = CommutePlaceKind.Work, Latitude = 52.2, Longitude = 5 },
        ];

        var joined = TripJoin.Join(Part(52.0, 52.1, 0), Part(52.1, 52.2, 20), places);

        Assert.Equal((TripPurpose.Commute, false), (joined.Purpose, joined.PurposeIsManual));
    }

    [Fact]
    public void NotesHiddenAndEdits_FollowTheJoinRules()
    {
        var joined = TripJoin.Join(
            Part(52.0, 52.1, 0, notes: "Fuel", hidden: true, edited: true), Part(52.1, 52.2, 20, notes: "Work", hidden: false), []);

        Assert.Equal("Fuel / Work", joined.Notes);
        Assert.False(joined.IsHidden);
        Assert.Equal(4, joined.OriginalPoints!.Count);
        Assert.Equal(T0.AddDays(1), joined.EditedAt);
    }
}
