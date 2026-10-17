using System.Net;
using System.Threading.RateLimiting;
using GarageStack.Api.Endpoints;
using GarageStack.Api.Security;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Events;

namespace GarageStack.Api.Hosting;

/// <summary>
/// The pieces of the Api's request pipeline that need more than a line of setup, so Program.cs
/// reads as the order things happen in rather than as their details.
/// </summary>
internal static class ApiHostingExtensions
{
    private const int DefaultGlobalPermitsPerMinute = 120;

    private const string LoginPolicy = "login";

    // The address nginx saw the request come from, kept before the forwarded headers are unwrapped.
    internal const string PeerItemKey = "GarageStack.Peer";

    // Failed or not, sign-ins through one device in front of nginx per window when no proxies are
    // configured: three times one visitor's limit, so a household behind an outer proxy never
    // notices it, while a LAN device forging X-Forwarded-For stops here instead of never.
    private const int LoginPermitsPerPeer = 30;

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

        var proxiesConfigured = TrustedProxiesFrom(configuration) is { Length: > 0 };

        return services.AddRateLimiter(opts =>
        {
            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            var global = PartitionedRateLimiter.Create(FixedWindowPerIp(TimeSpan.FromMinutes(1), globalPermitsPerMinute));
            opts.GlobalLimiter = proxiesConfigured
                ? global
                : PartitionedRateLimiter.CreateChained(global, LoginPerPeerLimiter());

            // Tighter, endpoint-specific limit on login to slow down credential-stuffing attempts.
            // Composes with (i.e. is enforced in addition to) the global limiter above.
            opts.AddPolicy(LoginPolicy, FixedWindowPerIp(TimeSpan.FromMinutes(5), permitLimit: 10));

            // Starting an OIDC sign-in submits no credentials, so it needs no brute-force limit --
            // but it does redirect to the identity provider, and a redirect loop caused by a
            // misconfiguration should not hammer it. Loose enough that auto-login plus a few page
            // reloads never trips it.
            opts.AddPolicy("oidc-login", FixedWindowPerIp(TimeSpan.FromMinutes(5), permitLimit: 30));

            // Tighter limit on the widget endpoint to slow down guessing WIDGET_API_KEY, which the
            // global limiter alone (120/min by default) would allow at a much higher rate. Still generous
            // enough for a handful of dashboard widgets behind the same NAT polling every 30s.
            opts.AddPolicy("widget", FixedWindowPerIp(TimeSpan.FromMinutes(5), permitLimit: 60));

            // Browser error reports: a page stuck in an error loop must not fill the log files.
            // The page itself caps what it sends; this caps what a misbehaving one could.
            opts.AddPolicy("client-errors", FixedWindowPerIp(TimeSpan.FromMinutes(5), permitLimit: 30));

            // A full export reads every row the car ever sent: a few a quarter of an hour is plenty.
            opts.AddPolicy("export", FixedWindowPerIp(TimeSpan.FromMinutes(15), permitLimit: 5));
        });
    }

    // One fixed-window limiter per client IP. Every policy above differs only in window and limit.
    private static Func<HttpContext, RateLimitPartition<string>> FixedWindowPerIp(TimeSpan window, int permitLimit) =>
        httpContext => FixedWindow(httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", window, permitLimit);

    private static RateLimitPartition<string> FixedWindow(string key, TimeSpan window, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = window,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    /// <summary>
    /// With no proxies configured every private address is believed as a proxy, so a device on the
    /// LAN can put any address it likes in X-Forwarded-For and look like a new visitor on each
    /// sign-in attempt. The address nginx saw (<see cref="PeerItemKey"/>) cannot be forged that way,
    /// so sign-ins are also counted per that address. Every other request passes untouched.
    /// </summary>
    internal static PartitionedRateLimiter<HttpContext> LoginPerPeerLimiter() =>
        PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == LoginPolicy
                ? FixedWindow(
                    httpContext.Items[PeerItemKey] as string ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    TimeSpan.FromMinutes(5),
                    LoginPermitsPerPeer)
                : RateLimitPartition.GetNoLimiter(""));

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
    /// One log line per request, except for the health probes Docker and uptime monitors send
    /// every few seconds: those would fill the log files with nothing. A failing readiness check
    /// answers 503 on every probe; whatever failed is logged where it fails. The path is logged
    /// with its VIN shortened, as everywhere else at Information level.
    /// </summary>
    public static WebApplication UseGarageStackRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(opts =>
        {
            opts.GetLevel = RequestLogLevel;
            opts.GetMessageTemplateProperties = RequestLogProperties;
        });
        return app;
    }

    // The properties of Serilog's default "HTTP {RequestMethod} {RequestPath} responded
    // {StatusCode} in {Elapsed} ms" line, with the path redacted.
    internal static IEnumerable<LogEventProperty> RequestLogProperties(
        HttpContext context, string requestPath, double elapsedMs, int statusCode) =>
    [
        new("RequestMethod", new ScalarValue(context.Request.Method)),
        new("RequestPath", new ScalarValue(LogRedaction.RequestPath(requestPath))),
        new("StatusCode", new ScalarValue(statusCode)),
        new("Elapsed", new ScalarValue(elapsedMs)),
    ];

    internal static LogEventLevel RequestLogLevel(HttpContext context, double elapsedMs, Exception? ex)
    {
        if (ex is not null) return LogEventLevel.Error;
        if (HealthEndpoints.IsHealthProbe(context.Request.Path)) return LogEventLevel.Verbose;
        return context.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : LogEventLevel.Information;
    }

    /// <summary>
    /// Trusts X-Forwarded-For/-Proto from the configured proxies, or from the private address
    /// ranges when none are configured, so the client IP (rate limits) and scheme (cookies, OIDC
    /// redirects) are the browser's rather than nginx's.
    /// </summary>
    public static WebApplication UseGarageStackForwardedHeaders(this WebApplication app)
    {
        var trustedProxies = TrustedProxiesFrom(app.Configuration);
        var options = ForwardedHeadersOptionsFor(trustedProxies);
        if (trustedProxies is not { Length: > 0 })
        {
            if (!app.Environment.IsDevelopment())
                Log.Warning("ForwardedHeaders:TrustedProxies is not configured -- trusting all RFC 1918 ranges, " +
                            "so sign-ins are also limited per device in front of nginx. Set it to your proxy IP(s) " +
                            "to stop anything on the LAN from claiming another address.");

            // Kept before the unwrapping below replaces it, for the per-peer sign-in limit.
            app.Use((context, next) =>
            {
                context.Items[PeerItemKey] = NearestPeer(context, options);
                return next(context);
            });
        }

        app.UseForwardedHeaders(options);
        return app;
    }

    internal static string[]? TrustedProxiesFrom(IConfiguration configuration) =>
        configuration.GetSection("ForwardedHeaders:TrustedProxies").Get<string[]>();

    /// <summary>
    /// The address of whatever connected to nginx: the last X-Forwarded-For entry, which nginx
    /// appends itself, when the request comes through a trusted proxy; the connection's own address
    /// otherwise. Unlike the entries before it, a client cannot choose this one.
    /// </summary>
    internal static string NearestPeer(HttpContext context, ForwardedHeadersOptions options)
    {
        var connection = context.Connection.RemoteIpAddress;
        if (connection is null) return "unknown";
        if (connection.IsIPv4MappedToIPv6) connection = connection.MapToIPv4();

        var trusted = options.KnownProxies.Contains(connection) || options.KnownIPNetworks.Any(n => n.Contains(connection));
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!trusted || forwardedFor.Length == 0) return connection.ToString();

        var last = forwardedFor[(forwardedFor.LastIndexOf(',') + 1)..].Trim();
        return IPAddress.TryParse(last, out var peer) ? peer.ToString() : connection.ToString();
    }

    /// <summary>
    /// Which proxies' forwarded headers are believed: the configured addresses, or every private
    /// range when none are configured (nginx in a Docker network has one of those).
    /// </summary>
    internal static ForwardedHeadersOptions ForwardedHeadersOptionsFor(string[]? trustedProxies)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // Unwrap X-Forwarded-For hop by hop for as long as the hop is a trusted proxy, rather
            // than only the last one (the default limit of 1). Behind Traefik or NPM in front of
            // nginx, the last hop is the outer proxy, so with a limit of 1 every visitor shared its
            // address, and with it the login rate limit.
            ForwardLimit = null,
        };

        if (trustedProxies is { Length: > 0 })
        {
            // Prefer explicit proxy IPs to limit header-spoofing surface.
            foreach (var proxyIp in trustedProxies)
                if (IPAddress.TryParse(proxyIp, out var ip))
                    options.KnownProxies.Add(ip);
        }
        else
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
        }

        return options;
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
