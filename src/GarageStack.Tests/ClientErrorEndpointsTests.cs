using GarageStack.Api.Endpoints;

namespace GarageStack.Tests;

// The report is whatever the page sends, so what reaches the log is capped and kept on one line.
public class ClientErrorEndpointsTests
{
    [Fact]
    public void AReport_IsKeptToOneLine_SoItCannotForgeLogEntries()
    {
        var entry = ClientErrorEndpoints.ToLogEntry(new ClientErrorReport(
            Message: "boom\r\n[12:00:00 INF] Forged entry",
            Stack: "at a (app.js:1:2)\nat b (app.js:3:4)",
            Page: "/map",
            Source: "https://garage.example/assets/index.js:1:2"));

        Assert.Equal("boom [12:00:00 INF] Forged entry", entry.Message);
        Assert.Equal(" | at a (app.js:1:2) | at b (app.js:3:4)", entry.Stack);
        Assert.DoesNotContain('\n', entry.Message + entry.Stack + entry.Page + entry.Source);
    }

    [Fact]
    public void EveryField_IsCapped()
    {
        var entry = ClientErrorEndpoints.ToLogEntry(new ClientErrorReport(
            Message: new string('m', 5000),
            Stack: new string('s', 50_000),
            Page: "/" + new string('p', 5000),
            Source: null));

        Assert.Equal(ClientErrorEndpoints.MaxMessageLength, entry.Message!.Length);
        Assert.True(entry.Stack!.Length <= ClientErrorEndpoints.MaxStackLength + 3);
        Assert.Equal(ClientErrorEndpoints.MaxPathLength, entry.Page!.Length);
        Assert.Equal("?", entry.Source);
    }

    [Fact]
    public void AVinInThePage_IsShortened()
    {
        var entry = ClientErrorEndpoints.ToLogEntry(new ClientErrorReport("boom", null, "/api/vehicles/FAKEVN00000000001/status", null));

        Assert.Equal("/api/vehicles/***0001/status", entry.Page);
        Assert.Equal("", entry.Stack);
    }
}
