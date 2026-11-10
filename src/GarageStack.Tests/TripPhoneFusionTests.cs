using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TripPhoneFusionTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static TripPoint Car(int seconds, double lat = 52) => new(Start.AddSeconds(seconds), lat, 5, 40);
    private static PhoneFix Phone(int seconds, double lat = 52, string tracker = "fake-phone", double accuracy = 5) =>
        new(Start.AddSeconds(seconds), lat, 5, accuracy, 40, tracker);
    private static readonly TripPoint[] Points = [Car(0), Car(120), Car(240)];

    [Fact]
    public void FillsGapsAndCarWinsWithinTwentySeconds()
    {
        var result = TripPhoneFusion.Apply(Points, null, null,
            [Phone(0), Phone(20), Phone(60), Phone(120), Phone(220), Phone(240), Phone(300)]);
        Assert.Equal(PhoneFusionOutcome.Fused, result.Outcome);
        Assert.Equal([0, 60, 120, 240], result.Points.Select(p => (int)(p.RecordedAt - Start).TotalSeconds));
        Assert.Equal(TripPointSource.Phone, result.Points[1].Source);
    }

    [Theory]
    [InlineData(53, 5)]
    [InlineData(52, 51)]
    public void RejectsElsewhereOrPoorAccuracy(double latitude, double accuracy)
    {
        Assert.Equal(PhoneFusionOutcome.NoMatch, TripPhoneFusion.Apply(Points, null, null,
            [Phone(0, latitude, accuracy: accuracy), Phone(60, latitude, accuracy: accuracy),
             Phone(120, latitude, accuracy: accuracy), Phone(240, latitude, accuracy: accuracy)]).Outcome);
    }

    [Fact]
    public void TooFewCarFixesCannotConfirm()
    {
        Assert.Equal(PhoneFusionOutcome.NoMatch, TripPhoneFusion.Apply([Car(0)], null, null,
            [Phone(0), Phone(60)]).Outcome);
    }

    [Fact]
    public void SelectsOneBestTrackerAndDropsDuplicateTimes()
    {
        var result = TripPhoneFusion.Apply(Points, null, null,
            [Phone(0, tracker: "a"), Phone(120, tracker: "a"), Phone(240, 53, "a"), Phone(90, tracker: "a"),
             Phone(0, tracker: "b"), Phone(120, tracker: "b"), Phone(240, tracker: "b"), Phone(60, tracker: "b"), Phone(60, tracker: "b")]);
        Assert.Equal([0, 60, 120, 240], result.Points.Select(p => (int)(p.RecordedAt - Start).TotalSeconds));
    }

    [Fact]
    public void RecoversParkedStartButNeverCrossesPreviousTrip()
    {
        var result = TripPhoneFusion.Apply(Points, Car(-300), Start.AddSeconds(-120),
            [Phone(-180), Phone(-90), Phone(0), Phone(120), Phone(240)]);
        Assert.Equal(Start.AddSeconds(-90), result.Points[0].RecordedAt);
        Assert.Same(Points[^1], result.Points[^1]);
    }

    [Fact]
    public void PreservesAnchorAndRejectsLateStartAwayFromParking()
    {
        var anchored = new[] { Car(-180) with { Source = TripPointSource.Anchored } }.Concat(Points).ToArray();
        var result = TripPhoneFusion.Apply(anchored, Car(-300), null,
            [Phone(-240), Phone(-120, 53), Phone(-60), Phone(0), Phone(60), Phone(120), Phone(240)]);
        Assert.Same(anchored[0], result.Points[0]);
        Assert.DoesNotContain(result.Points, p => p.Source == TripPointSource.Phone && p.RecordedAt < Start);
    }

    [Fact]
    public void RequiresSeventyPercentOfPairedFixes()
    {
        var car = Enumerable.Range(0, 10).Select(i => Car(i * 120)).ToArray();
        foreach (var matches in new[] { 6, 7 })
        {
            var phone = Enumerable.Range(0, 10).Select(i => Phone(i * 120, i < matches ? 52 : 53))
                .Append(Phone(60)).ToArray();
            Assert.Equal(matches == 7 ? PhoneFusionOutcome.Fused : PhoneFusionOutcome.NoMatch,
                TripPhoneFusion.Apply(car, null, null, phone).Outcome);
        }
    }

    [Fact]
    public void EqualRatiosPreferMoreMatchesAndNeverMixTrackers()
    {
        var result = TripPhoneFusion.Apply(Points, null, null,
            [Phone(0, tracker: "a"), Phone(120, tracker: "a"), Phone(60, tracker: "a"),
             Phone(0, tracker: "b"), Phone(120, tracker: "b"), Phone(240, tracker: "b"), Phone(180, tracker: "b")]);
        Assert.Equal(Start.AddSeconds(180), Assert.Single(result.Points, p => p.Source == TripPointSource.Phone).RecordedAt);
    }

    [Fact]
    public void AccuracyBoundaryIsInclusiveAndOnePhoneFixCannotConfirmTwoCarFixes()
    {
        Assert.Equal(PhoneFusionOutcome.Fused, TripPhoneFusion.Apply(Points, null, null,
            [Phone(0, accuracy: 50), Phone(60, accuracy: 50), Phone(120, accuracy: 50)]).Outcome);
        Assert.Equal(PhoneFusionOutcome.NoMatch, TripPhoneFusion.Apply([Car(0), Car(120)], null, null,
            [Phone(60)]).Outcome);
    }

    [Fact]
    public void StationaryPhoneDoesNotFollowMovingCar()
    {
        Assert.Equal(PhoneFusionOutcome.NoMatch, TripPhoneFusion.Apply([Car(0), Car(120, 52.1), Car(240, 52.2)], null, null,
            [Phone(0), Phone(60), Phone(120), Phone(240)]).Outcome);
    }
}
