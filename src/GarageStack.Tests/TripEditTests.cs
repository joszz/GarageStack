using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TripEditTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    // A drive north, a fix every five minutes: about 5.6 km a leg.
    private static readonly TripPoint[] Recorded =
    [
        new(T0, 52.00, 5.0, 40),
        new(T0.AddMinutes(5), 52.05, 5.0, 80, TripPointSource.Anchored),
        new(T0.AddMinutes(10), 52.10, 5.0, 60),
    ];

    private static TripPointInput Input(TripPoint p) => new(new DateTimeOffset(p.RecordedAt), p.Latitude, p.Longitude);

    private static TripPointInput Input(DateTime at, double lat, double lon = 5.0) => new(new DateTimeOffset(at), lat, lon);

    private static TripEditProblem? Build(IReadOnlyList<TripPointInput>? input, out IReadOnlyList<TripPoint> points,
        DateTime? notBefore = null, DateTime? notAfter = null) =>
        TripEdit.TryBuild(input, Recorded, notBefore, notAfter, out points);

    // ── Which points are the car's ───────────────────────────────────────────

    [Fact]
    public void RecordedPoints_StayAsRecorded_SpeedAndSourceIncluded()
    {
        Assert.Null(Build([.. Recorded.Select(Input)], out var points));

        Assert.Equal(Recorded, points);
    }

    [Fact]
    public void AnyOtherPoint_IsPlacedByHand_WithoutASpeed()
    {
        // One added between the first two, and the last one moved.
        TripPointInput[] input =
        [
            Input(Recorded[0]),
            Input(T0.AddMinutes(2.5), 52.025, 5.001),
            Input(Recorded[1]),
            Input(T0.AddMinutes(10), 52.11),
        ];

        Assert.Null(Build(input, out var points));

        Assert.Equal(new TripPoint(T0.AddMinutes(2.5), 52.025, 5.001, null, TripPointSource.Manual), points[1]);
        Assert.Equal(new TripPoint(T0.AddMinutes(10), 52.11, 5.0, null, TripPointSource.Manual), points[3]);
        Assert.Equal(Recorded[1], points[2]);
    }

    [Fact]
    public void ARecordedPointSentWithMillisecondsOnly_IsStillTheRecordedOne()
    {
        // The database keeps microseconds; a browser's Date drops them.
        var precise = Recorded[0] with { RecordedAt = T0.AddTicks(4321) };
        TripPoint[] recorded = [precise, Recorded[2]];

        Assert.Null(TripEdit.TryBuild([Input(T0, 52.00), Input(Recorded[2])], recorded, null, null, out var points));

        Assert.Equal(precise, points[0]);
    }

    [Fact]
    public void ATimeWithAnOffset_IsKeptInUtc()
    {
        var input = new TripPointInput(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(2)), 52.00, 5.0);

        Assert.Null(Build([input, Input(Recorded[2])], out var points));

        Assert.Equal(DateTimeKind.Utc, points[0].RecordedAt.Kind);
        Assert.Equal(Recorded[0], points[0]);
    }

    // ── What is refused ──────────────────────────────────────────────────────

    [Fact]
    public void TooFewOrTooManyPoints_AreRefused()
    {
        var tooMany = Enumerable.Range(0, Recorded.Length + TripEdit.MaxAddedPoints + 1)
            .Select(i => Input(T0.AddSeconds(i), 52.00 + i * 1e-5))
            .ToList();

        Assert.Equal("trip.pointCount", Build(null, out _)?.Code);
        Assert.Equal("trip.pointCount", Build([Input(Recorded[0])], out _)?.Code);
        Assert.Equal("trip.pointCount", Build(tooMany, out _)?.Code);
        Assert.Null(Build(tooMany[..^1], out _));
    }

    public static TheoryData<TripPointInput> BadPoints => new()
    {
        new TripPointInput(null, 52.0, 5.0),
        new TripPointInput(new DateTimeOffset(T0), null, 5.0),
        new TripPointInput(new DateTimeOffset(T0), 52.0, null),
        new TripPointInput(new DateTimeOffset(T0), 90.5, 5.0),
        new TripPointInput(new DateTimeOffset(T0), 52.0, -180.5),
        new TripPointInput(new DateTimeOffset(T0), double.NaN, 5.0),
        new TripPointInput(new DateTimeOffset(T0), 52.0, double.PositiveInfinity),
    };

    [Theory]
    [MemberData(nameof(BadPoints))]
    public void APointWithoutATimeOrAPlace_IsRefused(TripPointInput bad) =>
        Assert.Equal("trip.pointInvalid", Build([bad, Input(Recorded[2])], out _)?.Code);

    [Fact]
    public void PointsGoingBackInTime_AreRefused() =>
        Assert.Equal("trip.pointOrder", Build([Input(Recorded[1]), Input(Recorded[0])], out _)?.Code);

    [Fact]
    public void PointsOutsideTheTripsEitherSide_AreRefused()
    {
        TripPointInput[] input = [.. Recorded.Select(Input)];

        Assert.Equal("trip.overlapsPrevious", Build(input, out _, notBefore: T0.AddMinutes(1))?.Code);
        Assert.Equal("trip.overlapsNext", Build(input, out _, notAfter: T0.AddMinutes(9))?.Code);
        Assert.Null(Build(input, out _, notBefore: T0, notAfter: T0.AddMinutes(10)));
    }

    [Fact]
    public void AStepFasterThanACarDrives_IsRefused()
    {
        // 22 km in a minute.
        TripPointInput[] jump = [Input(Recorded[0]), Input(T0.AddMinutes(1), 52.20), Input(Recorded[2])];
        // Somewhere else at the very same moment.
        TripPointInput[] sameTime = [Input(Recorded[0]), Input(T0, 52.01), Input(Recorded[2])];

        Assert.Equal("trip.pointTooFast", Build(jump, out _)?.Code);
        Assert.Equal("trip.pointTooFast", Build(sameTime, out _)?.Code);
    }

    [Fact]
    public void TwoRecordedFixesSharingATime_AreAcceptedAsTheSegmenterAcceptsThem()
    {
        TripPoint[] recorded = [Recorded[0], Recorded[1] with { RecordedAt = T0 }, Recorded[2]];

        Assert.Null(TripEdit.TryBuild([.. recorded.Select(Input)], recorded, null, null, out _));
    }

    // ── What follows from the points ─────────────────────────────────────────

    [Fact]
    public void Summarize_ReadsTheTripOffItsPoints()
    {
        var trip = TripEdit.Summarize(Recorded, id: 7);

        Assert.Equal((T0, T0.AddMinutes(10), 3, 7L), (trip.StartedAt, trip.EndedAt, trip.PointCount, trip.Id!.Value));
        Assert.Equal(11.12, trip.DistanceKm);
        Assert.Equal(80, trip.MaxSpeedKmh);
    }

    [Theory]
    [InlineData(52.0004, false)] // 44 m
    [InlineData(52.0006, true)]  // 67 m
    public void AddressMoved_PastFiftyMetres(double latitude, bool expected) =>
        Assert.Equal(expected, TripEdit.AddressMoved(52.0, 5.0, Recorded[0] with { Latitude = latitude }));

    [Fact]
    public void Arriving_AtAParkedFix_IsAtItsOwnTime()
    {
        var stop = new TripPoint(T0.AddMinutes(11), 52.101, 5.0, 0);

        Assert.Equal(T0.AddMinutes(11), TripEdit.Arriving(Recorded[2], stop, isParkedFix: true).SuggestedAt);
    }

    [Fact]
    public void Arriving_AtTheNextTripsStart_IsTheGapAtTownSpeed_ButNotAfterItLeft()
    {
        // 5.6 km at 25 km/h takes a little over 13 minutes.
        var near = new TripPoint(T0.AddHours(2), 52.15, 5.0, null);
        var leftSoon = near with { RecordedAt = T0.AddMinutes(15) };

        Assert.InRange(TripEdit.Arriving(Recorded[2], near, isParkedFix: false).SuggestedAt, T0.AddMinutes(23.2), T0.AddMinutes(23.5));
        Assert.Equal(T0.AddMinutes(15), TripEdit.Arriving(Recorded[2], leftSoon, isParkedFix: false).SuggestedAt);
    }

    [Theory]
    [InlineData(51.99, true)]    // 1.1 km: probably where it left from
    [InlineData(51.9995, false)] // 56 m: where it starts already
    [InlineData(51.50, false)]   // 56 km: something else happened
    public void Editor_SuggestsTheParkingSpotOnlyWithinTheAnchorLimits(double stopLatitude, bool expected)
    {
        var stop = new TripStop(stopLatitude, 5.0, T0.AddHours(-10), T0.AddMinutes(-5));

        var editor = TripEdit.Editor(1, Recorded, null, null, stop, null, null, null);

        Assert.Equal(expected, editor.Suggestion);
        Assert.Equal(Recorded.Length + TripEdit.MaxAddedPoints, editor.MaxPoints);
    }
}
