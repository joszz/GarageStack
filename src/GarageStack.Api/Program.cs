using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GarageStack.Api;
using GarageStack.Api.Authentication;
using GarageStack.Api.Endpoints;
using GarageStack.Api.Hubs;
using GarageStack.Api.Services;
using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Data;
using GarageStack.Data.Demo;
using GarageStack.Data.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = HostingExtensions.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // User secrets are loaded automatically in Development; also load them in Demo so local
    // dev secrets (e.g. OpenChargeMap:ApiKey) are available when running start-demo.ps1.
    if (builder.Environment.IsEnvironment("Demo"))
        builder.Configuration.AddUserSecrets<Program>(optional: true);

    builder.Services.AddGarageStackSerilog(builder.Configuration, "api");

    // Pin the key ring to a fixed, CWD-relative path (mirrors "logs/api-.log" above) instead of
    // relying on ASP.NET Core's implicit default, which resolves against the OS user profile.
    // A container running as a non-root user with no profile falls back to an in-memory key
    // ring there, silently invalidating every auth cookie on each restart.
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("keys"));

    var isDemoMode = builder.Configuration.GetValue<bool>("DEMO_MODE");

    builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");
    builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));

    if (isDemoMode)
    {
        Log.Information("DEMO MODE enabled -- using in-memory fake data, no database or MQTT required");
        builder.Services.AddDemoServices();
        builder.Services.AddSingleton<IMqttPublisher, DemoNoOpMqttPublisher>();
    }
    else
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured.");
        builder.Services.AddGarageStackData(connectionString);
        builder.Services.AddSingleton<MqttPublisher>();
        builder.Services.AddSingleton<IMqttPublisher>(sp => sp.GetRequiredService<MqttPublisher>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttPublisher>());
        builder.Services.AddHostedService<TelemetryNotificationService>();
    }
    builder.Services.AddOpenApi(opts =>
    {
        opts.AddDocumentTransformer((doc, _, _) =>
        {
            doc.Info = new()
            {
                Title = "GarageStack API",
                Version = "v1",
                Description = "REST API for GarageStack -- vehicle telemetry, statistics, and notifications.",
            };
            return Task.CompletedTask;
        });
    });
    builder.Services.ConfigureHttpJsonOptions(opts =>
    {
        opts.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        opts.SerializerOptions.Converters.Add(new FiniteDoubleConverter());
    });

    builder.Services.AddTyrePressureThresholds(builder.Configuration);
    builder.Services.AddHvBatteryCapacity(builder.Configuration);

    builder.Services.AddMemoryCache();
    builder.Services.AddScoped<ChargingStationService>();
    builder.Services.AddScoped<PoiService>();
    builder.Services.AddScoped<GeocodeService>();
    builder.Services.AddSingleton<VehicleCommandGate>();

    builder.Services.AddSignalR();

    builder.Services.AddGarageStackAuthentication(builder.Configuration, builder.Environment);

    // Requests per minute per client IP across the whole API. Configurable because the right
    // number depends on the deployment: a household sharing one NAT address, or a browser test
    // run driving several pages in parallel, bursts well past what a single tab needs.
    var globalPermitsPerMinute = builder.Configuration.IntegerOrDefault("RateLimits:GlobalPerMinute", 120);
    if (globalPermitsPerMinute < 1)
    {
        throw new InvalidOperationException(
            $"RateLimits:GlobalPerMinute must be at least 1, but was {globalPermitsPerMinute}.");
    }

    builder.Services.AddRateLimiter(opts =>
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

    // The browser origins allowed to call the API: CORS for cross-origin deployments and the
    // CSRF origin check below. Read once here; it is fixed for the process lifetime.
    var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];

    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(p =>
        {
            if (builder.Environment.IsDevelopment())
                p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            else
                p.WithOrigins(allowedOrigins)
                 .AllowAnyHeader()
                 .AllowAnyMethod()
                 .AllowCredentials();
        }));

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (isDemoMode)
            await DemoSeeder.SeedAsync(db);
        else
            await db.Database.MigrateAsync();
    }

    app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"error\":\"Internal server error\"}");
    }));

    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    var trustedProxies = app.Configuration.GetSection("ForwardedHeaders:TrustedProxies").Get<string[]>();
    if (trustedProxies is { Length: > 0 })
    {
        // Prefer explicit proxy IPs to limit header-spoofing surface.
        foreach (var proxyIp in trustedProxies)
            if (System.Net.IPAddress.TryParse(proxyIp, out var ip))
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
        forwardedOptions.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
        forwardedOptions.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
        forwardedOptions.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
#pragma warning restore ASPDEPR005
    }
    app.UseForwardedHeaders(forwardedOptions);
    app.UseSerilogRequestLogging();
    app.UseCors();

    // Rate limiting runs before the CSRF origin check so that a flood of requests with a
    // spoofed/mismatched Origin gets throttled instead of generating unbounded warning-log
    // volume below.
    app.UseRateLimiter();

    // Defense-in-depth: when an Origin header is present on a state-changing request,
    // verify it matches a configured allowed origin. SameSite=Strict is the primary CSRF
    // protection; this adds an explicit server-side check for deployments where that alone
    // is not sufficient (e.g., same-site subdomain compromise).
    if (!app.Environment.IsDevelopment() &&
        allowedOrigins.Any(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
    {
        Log.Warning(
            "CORS_ORIGIN contains 'localhost' ({Origins}). " +
            "Requests from other devices on the LAN will be rejected with 403. " +
            "Set CORS_ORIGIN to the address you use to reach the app from those devices, " +
            "e.g. http://192.168.1.100:8080",
            string.Join(", ", allowedOrigins));
    }

    var enforceOriginCheck = !app.Environment.IsDevelopment();
    var allowedOriginsForLog = string.Join(", ", allowedOrigins);
    app.Use(async (ctx, next) =>
    {
        if (enforceOriginCheck && IsStateChanging(ctx.Request.Method))
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) && !CsrfPolicy.IsOriginAllowed(origin, allowedOrigins))
            {
                Log.Warning(
                    "CSRF origin check failed: request Origin '{Origin}' not in allowed list ({Allowed}). " +
                    "If you are accessing from a LAN device, set CORS_ORIGIN to match the address in your browser.",
                    origin, allowedOriginsForLog);
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(
                    "{\"error\":\"Origin not allowed. Set CORS_ORIGIN to the address you use to reach the app.\"}");
                return;
            }
        }
        await next(ctx);
    });

    app.UseRequestLocalization(new RequestLocalizationOptions
    {
        DefaultRequestCulture = new RequestCulture("en"),
        SupportedCultures = [new CultureInfo("en"), new CultureInfo("nl")],
        SupportedUICultures = [new CultureInfo("en"), new CultureInfo("nl")],
    });
    app.UseAuthentication();
    app.UseAuthorization();

    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Demo"))
    {
        app.MapOpenApi();
        app.MapScalarApiReference(opts => opts
            .WithTitle("GarageStack API")
            .WithTheme(ScalarTheme.DeepSpace)
            .EnableDarkMode()
            .WithDynamicBaseServerUrl(true)
            .SortTagsAlphabetically()
            .SortOperationsByMethod());
    }

    app.MapHealthEndpoints();
    app.MapHub<TelemetryHub>("/hubs/telemetry").RequireAuthorization();
    app.MapAuthEndpoints();
    app.MapVehicleEndpoints();
    app.MapPushEndpoints();
    app.MapNotificationEndpoints();
    app.MapMaintenanceEndpoints();
    app.MapWidgetEndpoints();
    app.MapMapEndpoints();

    if (isDemoMode)
    {
        var demoTelemetry = app.Services.GetRequiredService<ITelemetryRepository>();
        app.MapDemoEndpoints(demoTelemetry);
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// One fixed-window limiter per client IP. Every policy above differs only in window and limit.
static Func<HttpContext, RateLimitPartition<string>> FixedWindowPerIp(TimeSpan window, int permitLimit) =>
    httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            Window = window,
            PermitLimit = permitLimit,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

static bool IsStateChanging(string method) =>
    HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
