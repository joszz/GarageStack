using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class DiagnosticsRedactionTests
{
    private const string FirstVin = "FAKEVN00000000001";
    private const string SecondVin = "FAKEVN00000000002";

    private static readonly DiagnosticsRedaction Redaction = new([(FirstVin, "ZZ-123-Z"), (SecondVin, null)]);

    [Fact]
    public void Scrub_NamesEachKnownCarByItsPlaceInTheList()
    {
        Assert.Equal(
            "car-1 parked, car-2 drove",
            Redaction.Scrub($"{FirstVin} parked, {SecondVin} drove"));
    }

    // The logs already shorten a VIN to its last four characters; those still say which car.
    [Fact]
    public void Scrub_NamesAKnownCarFromTheShortenedVinTheLogsWrite()
    {
        Assert.Equal(
            "Telemetry from car-2 dropped",
            Redaction.Scrub($"Telemetry from {LogRedaction.Vin(SecondVin)} dropped"));
    }

    [Fact]
    public void Scrub_ShortenedVinOfTwoCarsAlike_SaysNotWhichOne()
    {
        var alike = new DiagnosticsRedaction([("FAKEVNA0000000001", null), ("FAKEVNB0000000001", null)]);

        Assert.Equal("car-? parked", alike.Scrub("***0001 parked"));
    }

    [Fact]
    public void Scrub_ReplacesAVinItDoesNotKnow()
    {
        Assert.Equal("Unknown car car-?", Redaction.Scrub("Unknown car FAKEVN00000000009"));
    }

    [Theory]
    [InlineData("Plate ZZ123Z looked up")]
    [InlineData("Plate ZZ-123-Z looked up")]
    [InlineData("Plate zz 123 z looked up")]
    public void Scrub_ReplacesTheKnownPlate_HoweverItIsWritten(string text)
    {
        Assert.Equal("Plate [plate] looked up", Redaction.Scrub(text));
    }

    [Theory]
    [InlineData("Sign-in for user@example.com", "Sign-in for [email]")]
    [InlineData("Sign-in for u***@example.com", "Sign-in for [email]")]
    public void Scrub_ReplacesEmailAddresses_AlsoTheShortenedForm(string text, string expected)
    {
        Assert.Equal(expected, Redaction.Scrub(text));
    }

    [Fact]
    public void Scrub_ReplacesTheAccountInAGatewayTopic()
    {
        Assert.Equal(
            "Message on saic/[account]/vehicles/car-1/drivetrain/soc",
            Redaction.Scrub($"Message on saic/user@example.com/vehicles/{FirstVin}/drivetrain/soc"));
    }

    [Theory]
    [InlineData("Failed login attempt from IP=192.0.2.15", "Failed login attempt from IP=[ip]")]
    [InlineData("Connection from 2001:db8::1 refused", "Connection from [ip] refused")]
    [InlineData("Connection from 2001:db8:0:0:0:0:0:1 refused", "Connection from [ip] refused")]
    [InlineData("Connection from ::1 refused", "Connection from ::1 refused")]
    [InlineData("Connection from 127.0.0.1 refused", "Connection from 127.0.0.1 refused")]
    public void Scrub_ReplacesIpAddresses(string text, string expected)
    {
        Assert.Equal(expected, Redaction.Scrub(text));
    }

    [Fact]
    public void Scrub_ReplacesTheHostAndQueryOfAUrl_KeepsItsPath()
    {
        Assert.Equal(
            "Dawarich at https://[host]/api/v1/points?[query] did not answer",
            Redaction.Scrub("Dawarich at https://user:pass@dawarich.example.org:8443/api/v1/points?api_key=secret did not answer"));
    }

    [Fact]
    public void Scrub_ScrubsAUrlsPathLikeAnyText()
    {
        Assert.Equal(
            "GET https://[host]/api/vehicles/car-1/status",
            Redaction.Scrub($"GET https://garage.example.org/api/vehicles/{FirstVin}/status"));
    }

    [Theory]
    [InlineData("at http://localhost:5173/src/App.vue:12:3")]
    [InlineData("at http://127.0.0.1:5000/api/health")]
    [InlineData("at http://[::1]:5000/api/health")]
    public void Scrub_KeepsLoopbackUrls(string text)
    {
        Assert.Equal(text, Redaction.Scrub(text));
    }

    [Theory]
    [InlineData(@"in C:\Users\someone\src\Repo.cs:line 26", @"in C:\Users\[user]\src\Repo.cs:line 26")]
    [InlineData("in /home/someone/src/Repo.cs:line 26", "in /home/[user]/src/Repo.cs:line 26")]
    public void Scrub_ReplacesTheAccountNameInAHomeFolder(string text, string expected)
    {
        Assert.Equal(expected, Redaction.Scrub(text));
    }

    [Fact]
    public void Scrub_ReplacesCoordinates()
    {
        Assert.Equal("Parked at [coord], [coord]", Redaction.Scrub("Parked at 52.3702157, -4.8951679"));
    }

    // What a bug report is about stays readable: times, versions, ordinary numbers.
    [Theory]
    [InlineData("Gateway quiet since 10:30:38, retrying in 00:05:00")]
    [InlineData("Could not load Npgsql, Version=10.0.0.0, Culture=neutral")]
    [InlineData("SoC 81.5 % at 12.6 V, 3 commands queued")]
    [InlineData("GarageStack 0.19.1 on .NET 10.0.14")]
    [InlineData("HTTP GET /api/settings responded 500 in 1.0346 ms")]
    public void Scrub_LeavesTextWithoutIdentifiersAlone(string text)
    {
        Assert.Equal(text, Redaction.Scrub(text));
    }

    [Fact]
    public void Scrub_NullAndEmptyStayAsTheyAre()
    {
        Assert.Null(Redaction.Scrub(null));
        Assert.Equal("", Redaction.Scrub(""));
    }
}
