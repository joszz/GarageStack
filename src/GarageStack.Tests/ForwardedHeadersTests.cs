using System.Net;
using GarageStack.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
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

    // Traefik and the frontend container talk over a Docker network, whose addresses change when a
    // container is recreated: a range for that network keeps both believed, scheme included.
    [Fact]
    public async Task ARangeOfTrustedProxies_CoversTheDockerNetwork()
    {
        const string Traefik = "172.18.0.7";
        var context = await ThroughMiddleware(["192.168.1.30", "172.18.0.0/16"], Nginx, $"{Browser}, {Traefik}");

        Assert.Equal(IPAddress.Parse(Browser), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    // Only the outer proxy listed: nginx is not believed, so the request stays plain HTTP and the
    // OIDC redirect URI built from it does not match the registered https one.
    [Fact]
    public async Task WithNginxLeftOut_NothingForwardedIsBelieved()
    {
        var context = await ThroughMiddleware([OuterProxy], Nginx, $"{Browser}, {OuterProxy}");

        Assert.Equal(IPAddress.Parse(Nginx), context.Connection.RemoteIpAddress);
        Assert.NotEqual("https", context.Request.Scheme);
    }

    [Fact]
    public void UnreadableProxies_AreTheEntriesThatAreNeitherAnAddressNorARange()
    {
        string[] entries = ["192.168.1.30", " 172.18.0.0/16 ", "fd00::/8", "traefik", "172.18.0.0/99"];

        Assert.Equal(["traefik", "172.18.0.0/99"], ApiHostingExtensions.UnreadableProxies(entries));
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

    // With nothing configured, a LAN device's own private address is believed as a proxy, so the
    // address before it is whatever the device wrote. That is the gap the per-peer sign-in limit
    // closes; the peer is the address nginx appended, which the device cannot choose.
    [Fact]
    public async Task WithNothingConfigured_ALanDeviceCanClaimAnyAddress_ButNotItsPeer()
    {
        const string LanDevice = "192.168.1.50";
        var forged = $"198.51.100.7, {LanDevice}";
        var options = ApiHostingExtensions.ForwardedHeadersOptionsFor(null);
        var before = new DefaultHttpContext();
        before.Connection.RemoteIpAddress = IPAddress.Parse(Nginx);
        before.Request.Headers["X-Forwarded-For"] = forged;

        var peer = ApiHostingExtensions.NearestPeer(before, options);
        var context = await ThroughMiddleware(null, Nginx, forged);

        Assert.Equal(IPAddress.Parse("198.51.100.7"), context.Connection.RemoteIpAddress);
        Assert.Equal(LanDevice, peer);
    }

    [Theory]
    [InlineData(Browser, "198.51.100.7", Browser)] // not through a trusted proxy: its own address
    [InlineData(Nginx, "", Nginx)] // nothing forwarded
    [InlineData(Nginx, "not-an-address", Nginx)]
    [InlineData(Nginx, $"{Browser}, {OuterProxy}", OuterProxy)]
    public void NearestPeer_IsWhatNginxSaw(string remote, string forwardedFor, string expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (forwardedFor.Length > 0) context.Request.Headers["X-Forwarded-For"] = forwardedFor;

        Assert.Equal(expected, ApiHostingExtensions.NearestPeer(context, ApiHostingExtensions.ForwardedHeadersOptionsFor(null)));
    }

    [Fact]
    public void LoginPerPeer_StopsADeviceThatKeepsChangingItsClaimedAddress()
    {
        using var limiter = ApiHostingExtensions.LoginPerPeerLimiter();

        HttpContext Attempt(int i, string policy)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse($"198.51.100.{i % 250}");
            context.Items[ApiHostingExtensions.PeerItemKey] = "192.168.1.50";
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EnableRateLimitingAttribute(policy)), policy));
            return context;
        }

        var granted = Enumerable.Range(0, 40).Count(i => limiter.AttemptAcquire(Attempt(i, "login")).IsAcquired);
        var otherEndpoint = limiter.AttemptAcquire(Attempt(41, "widget")).IsAcquired;

        Assert.Equal(30, granted);
        Assert.True(otherEndpoint);
    }
}
