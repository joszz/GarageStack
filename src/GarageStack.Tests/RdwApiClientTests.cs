using System.Net;
using GarageStack.Core.Configuration;
using GarageStack.Core.Models;
using GarageStack.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

/// <summary>
/// The RDW client against responses recorded from opendata.rdw.nl (Fixtures/Rdw): an MG EHS with
/// one open recall, a truck with two open recalls and one repaired, its APK run out and no
/// insurance on record, and a plate the RDW does not know. The files are the RDW's answers
/// exactly as they came, so the field names are the RDW's and not a guess at them.
/// </summary>
public class RdwApiClientTests
{
    private static readonly RdwOptions Enabled = new() { Enabled = true };

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rdw", name));

    private static RdwApiClient Client(FakeHttpHandler handler, RdwOptions? options = null) =>
        new(new FakeHttpClientFactory(handler), options ?? Enabled, NullLogger<RdwApiClient>.Instance);

    [Fact]
    public async Task Registration_ReadsTheFieldsTheCardShows()
    {
        var handler = new FakeHttpHandler(Fixture("registration-HPK98B.json"));

        var registration = await Client(handler).GetRegistrationAsync("HPK98B", TestContext.Current.CancellationToken);

        Assert.Equal(new RdwRegistration("MG", "MG EHS PLUG-IN HYBRID", new DateOnly(2028, 4, 26), true, true), registration);
        Assert.Equal("https://opendata.rdw.nl/resource/m9d7-ebf2.json?kenteken=HPK98B", handler.RequestUris.Single());
    }

    [Fact]
    public async Task Registration_ReadsNoAsFalse()
    {
        var handler = new FakeHttpHandler(Fixture("registration-00BLS4.json"));

        var registration = await Client(handler).GetRegistrationAsync("00BLS4", TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2025, 12, 8), registration!.ApkExpiresOn);
        Assert.False(registration.WamInsured);
        Assert.True(registration.OpenRecallIndicator);
    }

    // Socrata answers a plate it does not know with an empty array: not found, not an error.
    [Fact]
    public async Task Registration_UnknownPlate_IsNotFound()
    {
        var handler = new FakeHttpHandler(Fixture("registration-unknown.json"));

        Assert.Null(await Client(handler).GetRegistrationAsync("ZZ999Z", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecallStatuses_ReadOpenAndRepairedAlike()
    {
        var handler = new FakeHttpHandler(Fixture("recall-status-00BLS4.json"));

        var statuses = await Client(handler).GetRecallStatusesAsync("00BLS4", TestContext.Current.CancellationToken);

        Assert.Equal(
            [new("MGP240278", "O"), new("MGP260056", "P"), new("MGP260068", "O")],
            statuses);
        Assert.Equal("https://opendata.rdw.nl/resource/t49b-isb7.json?kenteken=00BLS4", handler.RequestUris.Single());
    }

    [Fact]
    public async Task RecallDetails_ReadDefectRiskRepairAndRating()
    {
        var handler = new FakeHttpHandler(Fixture("recall-details-HPK98B.json"));

        var details = Assert.Single(await Client(handler).GetRecallDetailsAsync(["MGP230061"], TestContext.Current.CancellationToken));

        Assert.Equal("MGP230061", details.Code);
        Assert.StartsWith("De aardingskabel van de hoogvoltkachel", details.Defect);
        Assert.StartsWith("Een verhoogde overgangsweerstand", details.Risk);
        Assert.StartsWith("De voertuigeigenaar wordt verzocht een afspraak te maken", details.Repair);
        Assert.Equal("ERN", details.Severity);
        Assert.Equal(
            "https://opendata.rdw.nl/resource/j9yg-7rg9.json?$where=referentiecode_rdw in('MGP230061')",
            Uri.UnescapeDataString(handler.RequestUris.Single()));
    }

    [Fact]
    public async Task RecallDetails_AskForEveryCodeInOneRequest()
    {
        var handler = new FakeHttpHandler(Fixture("recall-details-00BLS4.json"));

        var details = await Client(handler).GetRecallDetailsAsync(["MGP240278", "MGP260068"], TestContext.Current.CancellationToken);

        Assert.Equal(["MGP240278", "MGP260068"], details.Select(d => d.Code).Order());
        Assert.Equal(["GEM", "ERN"], details.OrderBy(d => d.Code).Select(d => d.Severity));
        Assert.Equal(1, handler.CallCount);
    }

    // The codes go into a SoQL filter: one that is not shaped like the RDW's own never gets there.
    [Fact]
    public async Task RecallDetails_CodeThatIsNotACode_IsNotSent()
    {
        var handler = new FakeHttpHandler("[]");

        var details = await Client(handler).GetRecallDetailsAsync(["X') OR (1=1"], TestContext.Current.CancellationToken);

        Assert.Empty(details);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task AppToken_IsSentWhenConfigured()
    {
        var handler = new FakeHttpHandler(Fixture("registration-unknown.json"));

        await Client(handler, new RdwOptions { Enabled = true, AppToken = "token-123" })
            .GetRegistrationAsync("ZZ999Z", TestContext.Current.CancellationToken);

        Assert.Equal(["token-123"], handler.RequestHeaders.Single().GetValues("X-App-Token"));
    }

    [Fact]
    public async Task AppToken_IsLeftOutWithoutOne()
    {
        var handler = new FakeHttpHandler(Fixture("registration-unknown.json"));

        await Client(handler).GetRegistrationAsync("ZZ999Z", TestContext.Current.CancellationToken);

        Assert.False(handler.RequestHeaders.Single().Contains("X-App-Token"));
    }

    // Asked to slow down, the client stops asking for a while rather than hammer the limit.
    [Fact]
    public async Task Throttled_FailsAndBacksOffWithoutAskingAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new FakeHttpHandler("", HttpStatusCode.TooManyRequests);
        var client = Client(handler);

        await Assert.ThrowsAsync<RdwUnavailableException>(() => client.GetRegistrationAsync("HPK98B", ct));
        await Assert.ThrowsAsync<RdwUnavailableException>(() => client.GetRecallStatusesAsync("HPK98B", ct));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ServerError_IsUnavailable()
    {
        var handler = new FakeHttpHandler("oops", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RdwUnavailableException>(
            () => Client(handler).GetRegistrationAsync("HPK98B", TestContext.Current.CancellationToken));

        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task NotJson_IsUnavailable()
    {
        var handler = new FakeHttpHandler("<html>maintenance</html>");

        await Assert.ThrowsAsync<RdwUnavailableException>(
            () => Client(handler).GetRegistrationAsync("HPK98B", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SwitchedOff_SendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new FakeHttpHandler(Fixture("registration-HPK98B.json"));
        var client = Client(handler, new RdwOptions());

        await Assert.ThrowsAsync<RdwUnavailableException>(() => client.GetRegistrationAsync("HPK98B", ct));
        await Assert.ThrowsAsync<RdwUnavailableException>(() => client.GetRecallStatusesAsync("HPK98B", ct));
        await Assert.ThrowsAsync<RdwUnavailableException>(() => client.GetRecallDetailsAsync(["MGP230061"], ct));

        Assert.Equal(0, handler.CallCount);
    }
}
