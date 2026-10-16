using System.Net;
using GarageStack.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageStack.Tests;

// The client address the rate limiter partitions on. nginx in the frontend container appends the
// address it saw to X-Forwarded-For, so behind an outer proxy (Traefik, NPM) the chain has two
// proxy hops: "browser, outer proxy".
public class ForwardedHeadersTests
{
    private const string Nginx = "172.18.0.3";
    private const string OuterProxy = "192.168.1.30";
    private const string Browser = "203.0.113.5";

    private static async Task<HttpContext> ThroughMiddleware(string[]? trustedProxies, string remote, string forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(ApiHostingExtensions.ForwardedHeadersOptionsFor(trustedProxies)));
        await middleware.Invoke(context);
        return context;
    }

    [Fact]
    public async Task BehindAnOuterProxy_TheBrowsersAddressIsTheClient()
    {
        var context = await ThroughMiddleware(null, Nginx, $"{Browser}, {OuterProxy}");

        Assert.Equal(IPAddress.Parse(Browser), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task WithBothProxiesConfigured_TheBrowsersAddressIsTheClient()
    {
        var context = await ThroughMiddleware([Nginx, OuterProxy], Nginx, $"{Browser}, {OuterProxy}");

        Assert.Equal(IPAddress.Parse(Browser), context.Connection.RemoteIpAddress);
    }

    // A forwarded address is only believed from a trusted hop: a browser cannot claim to be
    // someone else by sending its own X-Forwarded-For through nginx.
    [Fact]
    public async Task AnAddressAddedBeforeAnUntrustedHop_IsIgnored()
    {
        var context = await ThroughMiddleware([Nginx], Nginx, $"198.51.100.7, {Browser}");

        Assert.Equal(IPAddress.Parse(Browser), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task FromAnUntrustedPeer_NothingIsBelieved()
    {
        var context = await ThroughMiddleware([Nginx], Browser, "198.51.100.7");

        Assert.Equal(IPAddress.Parse(Browser), context.Connection.RemoteIpAddress);
    }
}
