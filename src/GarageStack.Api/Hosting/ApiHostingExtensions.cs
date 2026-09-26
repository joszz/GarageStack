using System.Net;
using System.Threading.RateLimiting;
using GarageStack.Api.Security;
using GarageStack.Core.Configuration;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;

namespace GarageStack.Api.Hosting;

/// <summary>
/// The pieces of the Api's request pipeline that need more than a line of setup, so Program.cs
/// reads as the order things happen in rather than as their details.
/// </summary>
internal static class ApiHostingExtensions
{
    private const int DefaultGlobalPermitsPerMinute = 120;

    /// <summary>
    /// Requests per minute per client IP across the whole API, plus tighter policies for the
    /// endpoints worth guessing at. The global budget is configurable because the right number
    /// depends on the deployment: a household sharing one NAT address, or a browser test run
    /// driving several pages in parallel, bursts well past what a single tab needs.
    /// </summary>
    public static IServiceCollection AddGarageStackRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        var globalPermitsPerMinute = configuration.IntegerOrDefault("RateLimits:GlobalPerMinute", DefaultGlobalPermitsPerMinute);
        if (globalPermitsPerMinute < 1)
        {
            throw new InvalidOperationException(
                $"RateLimits:GlobalPerMinute must be at least 1, but was {globalPermitsPerMinute}.");
        }

        return services.AddRateLimiter(opts =>
        {
            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opts.GlobalLimiter = PartitionedRateLimiter.Create(FixedWindowPerIp(TimeSpan.FromMinutes(1), globalPermitsPerMinute));

            // Tighter, endpoint-specific limit on login to slow down credential-stuffing attempts.
            // Composes with (i.e. is enforced in addition to) the global limiter above.
            opts.AddPolicy("login", FixedWindowPerIp(TimeSpan.FromMinutes(5), permitLimit: 10));

            // Starting an OIDC sign-in submits no credentials, so it needs no brute-force limit --
            // but it does redirect to the identity provider, and a redirect loop caused by a
            // misconfiguration should not hammer it. Loose enough that auto-login plus a few page
            // reloads never trips it.
            opts.AddPolicy("oidc-login", FixedWindowPerIp(TimeSpan.FromMinutes(5), permitLimit: 30));

            // Tighter limit on the widget endpoint to slow down guessing WIDGET_API_KEY, which the
            // global limiter alone (120/min by default) would allow at a much higher rate. Still generous
            // enough for a handful of dashboard widgets behind the same NAT polling every 30s.
            opts.AddPolicy("widget", FixedWindowPerIp(TimeSpan.FromMinutes(5), permitLimit: 60));
        });
    }

    // One fixed-window limiter per client IP. Every policy above differs only in window and limit.
    private static Func<HttpContext, RateLimitPartition<string>> FixedWindowPerIp(TimeSpan window, int permitLimit) =>
        httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = window,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    /// <summary>
    /// CORS for the configured browser origins; any origin in Development, where the Vite dev
    /// server and the API run on different ports.
    /// </summary>
    public static IServiceCollection AddGarageStackCors(
        this IServiceCollection services, IReadOnlyList<string> allowedOrigins, IHostEnvironment environment) =>
        services.AddCors(opts =>
            opts.AddDefaultPolicy(p =>
            {
                if (environment.IsDevelopment())
                    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
                else
                    p.WithOrigins([.. allowedOrigins])
                     .AllowAnyHeader()
                     .AllowAnyMethod()
                     .AllowCredentials();
            }));

    /// <summary>
    /// Trusts X-Forwarded-For/-Proto from the configured proxies, or from the private address
    /// ranges when none are configured, so the client IP (rate limits) and scheme (cookies, OIDC
    /// redirects) are the browser's rather than nginx's.
    /// </summary>
    public static WebApplication UseGarageStackForwardedHeaders(this WebApplication app)
    {
        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        var trustedProxies = app.Configuration.GetSection("ForwardedHeaders:TrustedProxies").Get<string[]>();
        if (trustedProxies is { Length: > 0 })
        {
            // Prefer explicit proxy IPs to limit header-spoofing surface.
            foreach (var proxyIp in trustedProxies)
                if (IPAddress.TryParse(proxyIp, out var ip))
                    forwardedOptions.KnownProxies.Add(ip);
        }
        else
        {
            // Fallback: trust all RFC 1918 ranges so nginx in a Docker network is recognised.
            // Set ForwardedHeaders:TrustedProxies in production to restrict to the actual proxy IP.
            if (!app.Environment.IsDevelopment())
                Log.Warning("ForwardedHeaders:TrustedProxies is not configured -- trusting all RFC 1918 ranges. " +
                            "Set this to your proxy IP(s) to prevent forwarded-header spoofing.");
#pragma warning disable ASPDEPR005
            forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
            forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
            forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
#pragma warning restore ASPDEPR005
        }

        app.UseForwardedHeaders(forwardedOptions);
        return app;
    }

    /// <summary>
    /// Defense-in-depth: when an Origin header is present on a state-changing request, it has to
    /// match a configured origin. SameSite=Strict is the primary CSRF protection; this adds an
    /// explicit server-side check for deployments where that alone is not sufficient (e.g., a
    /// compromised same-site subdomain). Off in Development, like the strict CORS policy.
    /// </summary>
    public static WebApplication UseCsrfOriginCheck(this WebApplication app, IReadOnlyList<string> allowedOrigins)
    {
        if (app.Environment.IsDevelopment())
            return app;

        if (allowedOrigins.Any(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            Log.Warning(
                "CORS_ORIGIN contains 'localhost' ({Origins}). " +
                "Requests from other devices on the LAN will be rejected with 403. " +
                "Set CORS_ORIGIN to the address you use to reach the app from those devices, " +
                "e.g. http://192.168.1.100:8080",
                string.Join(", ", allowedOrigins));
        }

        app.UseMiddleware<CsrfOriginMiddleware>(allowedOrigins);
        return app;
    }
}
