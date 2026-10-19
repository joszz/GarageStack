using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TripDtoTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private static TripDto Trip(params double?[] speeds) => new(
        0, T0, T0.AddMinutes(speeds.Length), 5, speeds.Length,
        [.. speeds.Select((speed, i) => new TripPoint(T0.AddMinutes(i), 52.0 + i * 0.01, 5.0, speed))],
        Id: 7);

    [Fact]
    public void Summary_ReadsTheSpeedsOffTheFixes()
    {
        var trip = Trip(0, 40, null, 80, 60, 0);

        Assert.Equal(80, trip.MaxSpeedKmh);
        // Standing still does not drag the average down: only the moving readings count.
        Assert.Equal(60, trip.AvgMovingSpeedKmh);
        Assert.Equal(3, trip.MovingSpeedSamples);
        Assert.Equal(52.05, trip.EndLatitude);
        Assert.Equal(5.0, trip.EndLongitude);
    }

    [Fact]
    public void Summary_WithoutSpeedReadings_HasNoSpeeds()
    {
        var trip = Trip(null, null);

        Assert.Null(trip.MaxSpeedKmh);
        Assert.Null(trip.AvgMovingSpeedKmh);
        Assert.Equal(0, trip.MovingSpeedSamples);
    }

    [Fact]
    public void Summary_WhileStandingStill_HasATopSpeedOfZeroAndNoAverage()
    {
        var trip = Trip(0, 0);

        Assert.Equal(0, trip.MaxSpeedKmh);
        Assert.Null(trip.AvgMovingSpeedKmh);
    }

    [Fact]
    public void ToSummary_KeepsEverythingButTheFixes()
    {
        var trip = Trip(30, 50);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(trip.ToSummary(), JsonSerializerOptions.Web));
        var root = json.RootElement;

        Assert.False(root.TryGetProperty("points", out _));
        Assert.Equal(7, root.GetProperty("id").GetInt64());
        Assert.Equal(2, root.GetProperty("pointCount").GetInt32());
        Assert.Equal(50, root.GetProperty("maxSpeedKmh").GetDouble());
        Assert.Equal(40, root.GetProperty("avgMovingSpeedKmh").GetDouble());
        Assert.Equal(2, root.GetProperty("movingSpeedSamples").GetInt32());
        Assert.Equal(52.01, root.GetProperty("endLatitude").GetDouble());
    }

    [Fact]
    public void FullTrip_ServesTheSummaryBesideTheFixes()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Trip(30, 50), JsonSerializerOptions.Web));
        var root = json.RootElement;

        Assert.Equal(2, root.GetProperty("points").GetArrayLength());
        Assert.Equal(50, root.GetProperty("maxSpeedKmh").GetDouble());
    }

    [Fact]
    public void ToCompact_PacksTheFixes_SoTheyUnpackAsTheyWere()
    {
        var started = T0.AddSeconds(-2);
        TripPoint[] points =
        [
            new(T0, 52.0912345, 5.1212345, null),
            new(T0.AddSeconds(40.5), 52.0923456, 5.1198765, 48.5),
            new(T0.AddSeconds(85), 52.0934567, 5.1187654, 0),
        ];
        var trip = new TripDto(3, started, T0.AddSeconds(85), 0.4, 3, points, Id: 7);

        var compact = trip.ToCompact();

        var line = PolylineCodec.Decode(compact.Line);
        var at = started;
        for (var i = 0; i < points.Length; i++)
        {
            at = at.AddMilliseconds(compact.IntervalsMs[i]);
            Assert.Equal(points[i].RecordedAt, at);
            Assert.Equal(points[i].Latitude, line[i].Lat, 6);
            Assert.Equal(points[i].Longitude, line[i].Lng, 6);
            Assert.Equal(points[i].Speed, compact.Speeds[i]);
        }
        Assert.Equal([2000L, 40500L, 44500L], compact.IntervalsMs);
        Assert.Equal(trip.ToSummary(), new TripSummaryDto(
            compact.Index, compact.Id, compact.StartedAt, compact.EndedAt, compact.DistanceKm, compact.PointCount,
            compact.EndLatitude, compact.EndLongitude, compact.MaxSpeedKmh, compact.AvgMovingSpeedKmh, compact.MovingSpeedSamples));
    }

    [Fact]
    public void ToCompact_TimesBetweenMilliseconds_StayWithinHalfOfOne_HoweverLongTheTrip()
    {
        // 40.6 ms apart: rounding each interval on its own would gain 0.4 ms a fix.
        var points = Enumerable.Range(0, 200)
            .Select(i => new TripPoint(T0.AddTicks(i * 406_000L), 52.0, 5.0, null))
            .ToList();
        var trip = new TripDto(0, T0, points[^1].RecordedAt, 1, points.Count, points);

        var compact = trip.ToCompact();

        var at = T0;
        for (var i = 0; i < points.Count; i++)
        {
            at = at.AddMilliseconds(compact.IntervalsMs[i]);
            Assert.Equal(points[i].RecordedAt, at, TimeSpan.FromMilliseconds(0.5));
        }
    }

    [Fact]
    public void ToCompact_ServesNoPointsArray()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Trip(30, 50).ToCompact(), JsonSerializerOptions.Web));
        var root = json.RootElement;

        Assert.False(root.TryGetProperty("points", out _));
        Assert.Equal(JsonValueKind.String, root.GetProperty("line").ValueKind);
        Assert.Equal(2, root.GetProperty("intervalsMs").GetArrayLength());
        Assert.Equal(2, root.GetProperty("speeds").GetArrayLength());
    }
}
