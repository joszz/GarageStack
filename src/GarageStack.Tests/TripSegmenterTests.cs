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
}
