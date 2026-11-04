using System.Text.Json;
using GarageStack.Api.Endpoints;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TripEndpointsTests
{
    // The options minimal APIs read request bodies and write responses with.
    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;

    [Theory]
    [InlineData(TripPurpose.Business, "business")]
    [InlineData(TripPurpose.Commute, "commute")]
    [InlineData(TripPurpose.Private, "private")]
    public void Purpose_TravelsAsItsLowerCaseName(TripPurpose purpose, string name)
    {
        Assert.Equal($"\"{name}\"", JsonSerializer.Serialize(purpose, Web));
        Assert.Equal(purpose, JsonSerializer.Deserialize<TripPurpose>($"\"{name}\"", Web));
    }

    [Fact]
    public void UpdateRequest_ReadsAPurposeAndNotes_OrNeither()
    {
        var set = JsonSerializer.Deserialize<TripLogUpdateRequest>("""{"purpose":"commute","notes":"Via the ring road"}""", Web);
        var cleared = JsonSerializer.Deserialize<TripLogUpdateRequest>("""{"purpose":null,"notes":null}""", Web);

        Assert.Equal(new TripLogUpdateRequest(TripPurpose.Commute, "Via the ring road"), set);
        Assert.Equal(new TripLogUpdateRequest(null, null), cleared);
    }

    [Theory]
    [InlineData("\"holiday\"")]
    [InlineData("\"0\"")]
    [InlineData("0")]
    public void UpdateRequest_UnknownPurpose_IsRefused(string purpose)
    {
        // Minimal APIs answer a body that fails to bind with 400, so a purpose outside the three
        // never reaches the database.
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TripLogUpdateRequest>($$"""{"purpose":{{purpose}}}""", Web));
    }

    [Fact]
    public void LogEntry_ServesPlacesWithTheKeysTheMapsPlacesUse()
    {
        var place = new PlaceAddress("Brink 2, Deventer", "Brink", "2", "Deventer", "7411 BT", "nl");
        var entry = new TripLogEntry(1, DateTime.UnixEpoch, DateTime.UnixEpoch, 1, 0, 0, 0, 0, null, null, place, null, TripPurpose.Private, null);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(entry, Web));
        var served = json.RootElement.GetProperty("startPlace");

        Assert.Equal(
            ["displayName", "road", "houseNumber", "city", "postcode", "countryCode"],
            served.EnumerateObject().Select(p => p.Name));
        Assert.Equal("private", json.RootElement.GetProperty("purpose").GetString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Client visit \n", "Client visit")]
    public void NormalizeNotes_TrimsAndTreatsBlankAsNone(string? notes, string? expected) =>
        Assert.Equal(expected, TripEndpoints.NormalizeNotes(notes));

    [Fact]
    public void Point_ServesItsSourceByName_AndLeavesItOutForTheCarsOwnFixes()
    {
        var at = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        using var reported = JsonDocument.Parse(JsonSerializer.Serialize(new TripPoint(at, 52, 5, 50), Web));
        using var placed = JsonDocument.Parse(JsonSerializer.Serialize(new TripPoint(at, 52, 5, null, TripPointSource.Manual), Web));

        Assert.False(reported.RootElement.TryGetProperty("source", out _));
        Assert.Equal("manual", placed.RootElement.GetProperty("source").GetString());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TripPoint>(
            """{"recordedAt":"2026-01-01T10:00:00Z","latitude":52,"longitude":5,"speed":null,"source":1}""", Web));
    }

    [Fact]
    public void CompactTrip_CarriesSourcesOnlyWhenAPointHasOne()
    {
        var at = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        TripPoint[] reported = [new(at, 52.00, 5, 50), new(at.AddMinutes(5), 52.05, 5, 50)];
        TripPoint[] anchored = [reported[0] with { Source = TripPointSource.Anchored }, reported[1]];

        using var plain = JsonDocument.Parse(JsonSerializer.Serialize(new TripDto(0, at, at, 5.56, 2, reported).ToCompact(), Web));
        using var mixed = JsonDocument.Parse(JsonSerializer.Serialize(new TripDto(0, at, at, 5.56, 2, anchored).ToCompact(), Web));

        Assert.False(plain.RootElement.TryGetProperty("sources", out _));
        Assert.Equal(["anchored", null], mixed.RootElement.GetProperty("sources").EnumerateArray().Select(s => s.GetString()));
    }

    [Fact]
    public void PointsRequest_ReadsTimesWithAnOffset_AndMissingFieldsAsNull()
    {
        var req = JsonSerializer.Deserialize<TripPointsRequest>(
            """{"points":[{"recordedAt":"2026-01-01T12:00:00+02:00","latitude":52.1,"longitude":5.2},{"latitude":52.2}]}""", Web);

        Assert.Equal(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), req!.Points![0].RecordedAt!.Value.UtcDateTime);
        Assert.Equal(new TripPointInput(null, 52.2, null), req.Points[1]);
    }
}
