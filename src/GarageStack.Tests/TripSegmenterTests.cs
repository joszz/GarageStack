using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TripSegmenterTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private static TripPoint Fix(double minutes, double lat, double? speed) =>
        new(T0.AddMinutes(minutes), lat, 0.0, speed);

    // Far enough past every fix that each segment has ended.
    private static readonly DateTime LongAfter = T0.AddDays(1);

    // ── Where one trip ends and the next begins ──────────────────────────────

    [Fact]
    public void BackToBackWithParkStop_ReturnsTwoTrips()
    {
        TripPoint[] fixes =
        [
            // Trip 1: A -> B
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50),
            // Parked at B for 10 minutes (> 5 min threshold)
            Fix(11, 51.60, 0), Fix(15, 51.60, 0), Fix(20, 51.60, 0),
            // Trip 2: B -> A
            Fix(21, 51.60, 50), Fix(25, 51.55, 50), Fix(30, 51.50, 50),
        ];

        Assert.Equal(2, TripSegmenter.Segment(fixes, LongAfter).Trips.Count);
    }

    [Fact]
    public void BriefTrafficStop_DoesNotSplitTrip()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50),
            // Brief stop at a traffic light (2 minutes < 5 min threshold)
            Fix(6, 51.55, 0), Fix(7, 51.55, 0),
            Fix(8, 51.55, 50), Fix(13, 51.60, 50),
        ];

        Assert.Single(TripSegmenter.Segment(fixes, LongAfter).Trips);
    }

    [Fact]
    public void NullSpeedParkingGap_SplitsIntoTwoTrips()
    {
        // GPS-only fixes (Speed = null) between two trips must still trigger the 5-minute parking split.
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50),
            // Gateway sends GPS-only updates (no speed topic) while parked.
            Fix(11, 51.60, null), Fix(15, 51.60, null), Fix(20, 51.60, null),
            // Second trip starts after >5 min of null-speed fixes.
            Fix(21, 51.60, 50), Fix(25, 51.55, 50), Fix(30, 51.50, 50),
        ];

        Assert.Equal(2, TripSegmenter.Segment(fixes, LongAfter).Trips.Count);
    }

    [Fact]
    public void BriefNullSpeedBlip_DoesNotSplitTrip()
    {
        // A single null-speed GPS update mid-trip (< 5 min) must not split the trip.
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50),
            Fix(6, 51.55, null), // brief null-speed blip
            Fix(7, 51.56, 50), Fix(12, 51.60, 50),
        ];

        Assert.Single(TripSegmenter.Segment(fixes, LongAfter).Trips);
    }

    [Fact]
    public void HalfAnHourWithoutFixes_SplitsIntoTwoTrips()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(36, 51.56, 50), Fix(40, 51.60, 50)];

        Assert.Equal(2, TripSegmenter.Segment(fixes, LongAfter).Trips.Count);
    }

    [Fact]
    public void ImplausibleJump_DiscardsTheTrip()
    {
        // 55 km in one minute is a positioning glitch, not a drive.
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(1, 52.00, 50), Fix(2, 52.01, 50)];

        Assert.Empty(TripSegmenter.Segment(fixes, LongAfter).Trips);
    }

    [Fact]
    public void DiscardedSegment_LeavesNoGapInTheIndexes()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50),
            Fix(6, 51.55, 0),
            // A 20 m shuffle across the car park: under the 0.1 km a trip needs.
            Fix(12, 51.5500, 5), Fix(13, 51.5502, 5),
            Fix(14, 51.5502, 0),
            Fix(20, 51.56, 50), Fix(25, 51.60, 50),
        ];

        var trips = TripSegmenter.Segment(fixes, LongAfter).Trips;

        Assert.Equal([0, 1], trips.Select(t => t.Index));
    }

    // ── Whether the last segment has ended ───────────────────────────────────

    [Fact]
    public void StillMovingAtTheHorizon_IsOpen()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50)];

        var result = TripSegmenter.Segment(fixes, T0.AddMinutes(11));

        Assert.Single(result.Trips);
        Assert.Equal(T0, result.OpenSince);
        Assert.Empty(result.Closed);
    }

    [Fact]
    public void ParkedForFiveMinutesByTheHorizon_IsClosed()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(6, 51.55, 0)];

        var result = TripSegmenter.Segment(fixes, T0.AddMinutes(11));

        Assert.Null(result.OpenSince);
        Assert.Single(result.Closed);
    }

    [Fact]
    public void ParkedForLessThanFiveMinutesByTheHorizon_IsOpen()
    {
        // The car may drive on within the five minutes, which keeps it the same trip.
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(6, 51.55, 0)];

        var result = TripSegmenter.Segment(fixes, T0.AddMinutes(10));

        Assert.Equal(T0, result.OpenSince);
    }

    [Fact]
    public void QuietForMoreThanHalfAnHourByTheHorizon_IsClosed()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50)];

        Assert.Equal(T0, TripSegmenter.Segment(fixes, T0.AddMinutes(35)).OpenSince);
        Assert.Null(TripSegmenter.Segment(fixes, T0.AddMinutes(35.1)).OpenSince);
    }

    [Fact]
    public void Closed_LeavesOutOnlyTheOpenTrip()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(6, 51.55, 0),
            Fix(20, 51.56, 50), Fix(25, 51.60, 50),
        ];

        var result = TripSegmenter.Segment(fixes, T0.AddMinutes(26));

        Assert.Equal(2, result.Trips.Count);
        Assert.Equal(T0.AddMinutes(20), result.OpenSince);
        Assert.Equal(T0, Assert.Single(result.Closed).StartedAt);
    }

    [Fact]
    public void NoFixes_NoTripsAndNothingOpen()
    {
        var result = TripSegmenter.Segment([], T0);

        Assert.Empty(result.Trips);
        Assert.Null(result.OpenSince);
    }

    // ── Where a trip ends: at the parked fix ─────────────────────────────────

    [Fact]
    public void ParkedFixAwayFromTheLastMovingOne_EndsTheTripThere()
    {
        // The car reports where it parked a poll after its last moving fix, about 560 m on.
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50),
            Fix(11, 51.605, 0), Fix(15, 51.605, 0),
        ];

        var trip = Assert.Single(TripSegmenter.Segment(fixes, LongAfter).Trips);

        Assert.Equal(new TripPoint(T0.AddMinutes(11), 51.605, 0.0, 0, TripPointSource.Anchored), trip.Points[^1]);
        Assert.Equal(T0.AddMinutes(11), trip.EndedAt);
        Assert.Equal(4, trip.PointCount);
        Assert.Equal(11.68, trip.DistanceKm);
    }

    [Fact]
    public void ParkedFixWithinDrift_LeavesTheTripEndingAtItsLastMovingFix()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50), Fix(11, 51.6003, 0)];

        var trip = Assert.Single(TripSegmenter.Segment(fixes, LongAfter).Trips);

        Assert.Equal(T0.AddMinutes(10), trip.EndedAt);
        Assert.All(trip.Points, p => Assert.Null(p.Source));
    }

    [Fact]
    public void ParkedFixImplyingAnImpossibleSpeed_LeavesTheTripAsItWas()
    {
        // 44 km in a minute: a bad fix, which must not cost the trip the way a bad moving fix does.
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50), Fix(11, 52.00, 0)];

        var trip = Assert.Single(TripSegmenter.Segment(fixes, LongAfter).Trips);

        Assert.Equal(3, trip.PointCount);
        Assert.Equal(51.60, trip.Points[^1].Latitude);
    }

    [Fact]
    public void StoppedThenQuiet_EndsTheTripAtTheParkedFix()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50), Fix(11, 51.605, 0),
            // The next morning, from the same spot.
            Fix(20 * 60, 51.605, 0),
        ];

        var trip = Assert.Single(TripSegmenter.Segment(fixes, LongAfter).Trips);

        Assert.Equal(TripPointSource.Anchored, trip.Points[^1].Source);
    }

    [Fact]
    public void TripStillOpen_DoesNotEndAtAStopItMayDriveOnFrom()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50), Fix(11, 51.605, 0)];

        var trip = Assert.Single(TripSegmenter.Segment(fixes, T0.AddMinutes(13)).Trips);

        Assert.Equal(3, trip.PointCount);
    }

    [Fact]
    public void OneMovingFixAndAParkedOne_IsNoTrip()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(1, 51.505, 0), Fix(10, 51.505, 0)];

        Assert.Empty(TripSegmenter.Segment(fixes, LongAfter).Trips);
    }

    // ── Where a trip starts: the parked fix before it ────────────────────────

    [Fact]
    public void ParkedBefore_IsTheLatestParkedFixAndSurvivesALongQuietPeriod()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50),
            Fix(11, 51.605, 0), Fix(14, 51.605, 0),
            // Overnight nothing, then the first fix arrives 5 km down the road.
            Fix(20 * 60, 51.65, 50), Fix(20 * 60 + 5, 51.70, 50),
        ];

        var result = TripSegmenter.Segment(fixes, LongAfter);

        Assert.Equal(2, result.Trips.Count);
        Assert.Equal([null, Fix(14, 51.605, 0)], result.ParkedBefore);
    }

    [Fact]
    public void ParkedBefore_IsNullWhenTheCarLastMovedBeforeItWentQuiet()
    {
        TripPoint[] fixes =
        [
            // A traffic stop mid-trip is not where the next trip leaves from.
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(6, 51.55, 0), Fix(8, 51.56, 50), Fix(10, 51.60, 50),
            Fix(20 * 60, 51.65, 50), Fix(20 * 60 + 5, 51.70, 50),
        ];

        var result = TripSegmenter.Segment(fixes, LongAfter);

        Assert.Equal([null, null], result.ParkedBefore);
    }

    [Fact]
    public void ParkedBefore_LinesUpWithTheTripsWhenASegmentIsDiscarded()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50),
            Fix(6, 51.55, 0),
            // A 20 m shuffle across the car park: no trip, and the car parked again after it.
            Fix(12, 51.5500, 5), Fix(13, 51.5502, 5),
            Fix(14, 51.5502, 0),
            Fix(20, 51.56, 50), Fix(25, 51.60, 50),
        ];

        var result = TripSegmenter.Segment(fixes, LongAfter);

        Assert.Equal([null, Fix(14, 51.5502, 0)], result.ParkedBefore);
    }

    [Fact]
    public void ParkedBefore_GivenForTheFixes_IsWhereTheirFirstTripLeftFrom()
    {
        var before = new TripPoint(T0.AddHours(-10), 51.49, 0.0, 0);

        var result = TripSegmenter.Segment([Fix(0, 51.50, 50), Fix(5, 51.55, 50)], LongAfter, before);

        Assert.Equal([before], result.ParkedBefore);
    }

    // ── Drives no poll caught (issue #640) ───────────────────────────────────

    private static TripPoint Anchored(TripPoint p) => p with { Source = TripPointSource.Anchored };

    [Fact]
    public void Unseen_StandingFarFromWhereItLastStoodAfterAQuietSpell_IsADrive()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50),
            Fix(11, 51.60, 0), Fix(14, 51.60, 0),
            // The gateway slept through the drive back: next seen parked 22 km north.
            Fix(20 * 60, 51.80, 0), Fix(20 * 60 + 2, 51.80, 0),
        ];

        var result = TripSegmenter.Segment(fixes, LongAfter);

        Assert.Single(result.Trips);
        var drive = Assert.Single(result.Unseen);
        Assert.Equal([Anchored(Fix(14, 51.60, 0)), Anchored(Fix(20 * 60, 51.80, 0))], drive.Points);
        Assert.Null(result.OpenSince);
    }

    [Fact]
    public void Unseen_FromTheParkedFixGivenForTheFixes()
    {
        var before = new TripPoint(T0.AddHours(-10), 51.60, 0.0, 0);

        var result = TripSegmenter.Segment([Fix(0, 51.80, 0)], LongAfter, before);

        Assert.Equal([Anchored(before), Anchored(Fix(0, 51.80, 0))], Assert.Single(result.Unseen).Points);
    }

    // A fix that wanders while parked is the same spot, even one from under a roof: 1.1 km.
    [Fact]
    public void Unseen_StandingWithinTheDriftMargin_IsNone()
    {
        TripPoint[] fixes = [Fix(0, 51.6000, 0), Fix(20 * 60, 51.6100, 0)];

        Assert.Empty(TripSegmenter.Segment(fixes, LongAfter).Unseen);
    }

    // A car that reports zero speed while driving is still polled every minute or so.
    [Fact]
    public void Unseen_FarApartButWithoutAQuietSpell_IsNone()
    {
        TripPoint[] fixes = [Fix(0, 51.60, 0), Fix(1, 51.62, 0), Fix(2, 51.64, 0)];

        Assert.Empty(TripSegmenter.Segment(fixes, LongAfter).Unseen);
    }

    // Longer than a sleeping car goes unpolled: the gateway was down, and anything can hide in that.
    [Fact]
    public void Unseen_AfterAQuietLongerThanASleepingCars_IsNone()
    {
        TripPoint[] fixes = [Fix(0, 51.60, 0), Fix(3 * 24 * 60, 51.80, 0)];

        Assert.Empty(TripSegmenter.Segment(fixes, LongAfter.AddDays(3)).Unseen);
    }

    // First seen waiting at a red light, then driving on: one trip, which may start where it stood.
    [Fact]
    public void Unseen_StandingTooBrieflyBeforeDrivingOn_IsPartOfTheTrip()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.60, 0),
            Fix(20 * 60, 51.63, 0),
            Fix(20 * 60 + 1, 51.64, 50), Fix(20 * 60 + 5, 51.70, 50), Fix(20 * 60 + 10, 51.75, 50),
            Fix(20 * 60 + 11, 51.75, 0),
        ];

        var result = TripSegmenter.Segment(fixes, LongAfter);

        Assert.Empty(result.Unseen);
        Assert.Single(result.Trips);
        Assert.Equal([Fix(0, 51.60, 0)], result.ParkedBefore);
    }

    // It may yet drive on, so the drive is cut again next time from where it was seen.
    [Fact]
    public void Unseen_NotStoodLongEnoughYet_HoldsTheOpenLine()
    {
        TripPoint[] fixes = [Fix(0, 51.60, 0), Fix(20 * 60, 51.80, 0)];

        var result = TripSegmenter.Segment(fixes, T0.AddMinutes(20 * 60 + 2));

        Assert.Empty(result.Unseen);
        Assert.Equal(T0.AddMinutes(20 * 60), result.OpenSince);
    }

    // One moving fix makes no trip, but is the poll that caught the drive on its way.
    [Fact]
    public void Unseen_WithOneMovingFixOnTheWay_KeepsIt()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.60, 0),
            Fix(20 * 60, 51.75, 60),
            Fix(20 * 60 + 15, 51.80, 0), Fix(20 * 60 + 17, 51.80, 0),
        ];

        var result = TripSegmenter.Segment(fixes, LongAfter);

        Assert.Empty(result.Trips);
        Assert.Equal(
            [Anchored(Fix(0, 51.60, 0)), Fix(20 * 60, 51.75, 60), Anchored(Fix(20 * 60 + 15, 51.80, 0))],
            Assert.Single(result.Unseen).Points);
    }

    // Caught once at speed, quiet again, then seen standing: the drive ends where it stands.
    [Fact]
    public void Unseen_CaughtOnTheWayThenQuiet_GoesOnToWhereTheCarIsSeenStanding()
    {
        TripPoint[] fixes = [Fix(0, 51.60, 0), Fix(10 * 60, 51.70, 80), Fix(12 * 60, 51.80, 0)];

        var result = TripSegmenter.Segment(fixes, LongAfter);

        Assert.Equal(
            [Anchored(Fix(0, 51.60, 0)), Fix(10 * 60, 51.70, 80), Anchored(Fix(12 * 60, 51.80, 0))],
            Assert.Single(result.Unseen).Points);
    }

    // A fix without a speed, and the next poll finding the car on that spot: it parked there.
    [Fact]
    public void Unseen_SeenAgainWhereThePollCaughtIt_EndsThere()
    {
        TripPoint[] fixes = [Fix(0, 51.60, 0), Fix(10 * 60, 51.70, null), Fix(34 * 60, 51.7001, null)];

        var result = TripSegmenter.Segment(fixes, LongAfter.AddDays(1));

        Assert.Equal([Anchored(Fix(0, 51.60, 0)), Anchored(Fix(10 * 60, 51.70, null))], Assert.Single(result.Unseen).Points);
    }

    // Still quiet at the horizon after the caught fix: the next fix decides, so it holds the line.
    [Fact]
    public void Unseen_CaughtOnTheWayAndQuietSince_HoldsTheOpenLine()
    {
        TripPoint[] fixes = [Fix(0, 51.60, 0), Fix(20 * 60, 51.75, 60)];

        var result = TripSegmenter.Segment(fixes, T0.AddMinutes(22 * 60));

        Assert.Empty(result.Unseen);
        Assert.Equal(T0.AddMinutes(20 * 60), result.OpenSince);
    }

    // A daily poll without a speed on the parking spot is the car still standing there, however
    // long the gap before it, so the next drive leaves from it.
    [Fact]
    public void Unseen_AfterAPollWithoutASpeedOnTheSameSpot_LeavesFromThatPoll()
    {
        TripPoint[] fixes = [Fix(0, 51.60, 0), Fix(24 * 60, 51.6001, null), Fix(47 * 60, 51.80, 0)];

        var result = TripSegmenter.Segment(fixes, LongAfter.AddDays(2));

        Assert.Equal([Anchored(Fix(24 * 60, 51.6001, null)), Anchored(Fix(47 * 60, 51.80, 0))], Assert.Single(result.Unseen).Points);
    }
}
