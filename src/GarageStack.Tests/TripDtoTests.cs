using System.Text.Json;
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
}
