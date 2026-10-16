using System.Net;
using GarageStack.Core.Configuration;
using GarageStack.Data.Services;
using Microsoft.Extensions.Logging;

namespace GarageStack.Tests;

// A rate limit used to throw for the tile that hit it and for every tile after it until the
// backoff ended, each logged twice with a stack trace (here and by the caller).
public class OcmApiClientTests
{
    [Fact]
    public async Task ARateLimit_LeavesTheTileForLater_AndPausesTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new FakeHttpHandler(((HttpStatusCode)429, ""), (HttpStatusCode.OK, "[]"));
        var logger = new RecordingLogger<OcmApiClient>();
        var client = new OcmApiClient(new FakeHttpClientFactory(handler), new OpenChargeMapOptions { ApiKey = "test-key" }, logger);

        var limited = await client.FetchChargingStationsAsync(104, 9, ct);
        var duringBackoff = await client.FetchChargingStationsAsync(104, 10, ct);

        Assert.Null(limited);
        Assert.Null(duringBackoff);
        Assert.Equal(1, handler.CallCount);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Null(warning.Exception);
    }

    [Fact]
    public async Task AnOrdinaryFailure_StillThrows_ForTheCallerToLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new FakeHttpHandler("oops", HttpStatusCode.InternalServerError);
        var logger = new RecordingLogger<OcmApiClient>();
        var client = new OcmApiClient(new FakeHttpClientFactory(handler), new OpenChargeMapOptions { ApiKey = "test-key" }, logger);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.FetchChargingStationsAsync(104, 9, ct));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }
}
