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
}
