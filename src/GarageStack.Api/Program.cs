using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Serialization;
using GarageStack.Api;
using GarageStack.Api.Endpoints;
using GarageStack.Api.Hubs;
using GarageStack.Api.Services;
using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Demo;
using GarageStack.Data.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using System.Text;
using System.Threading.RateLimiting;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // User secrets are loaded automatically in Development; also load them in Demo so local
    // dev secrets (e.g. OpenChargeMap:ApiKey) are available when running start-demo.ps1.
    if (builder.Environment.IsEnvironment("Demo"))
        builder.Configuration.AddUserSecrets<Program>(optional: true);

    var debugLogs = string.Equals(builder.Configuration["DEBUG_LOGS"], "true", StringComparison.OrdinalIgnoreCase);

    builder.Services.AddSerilog((_, config) =>
    {
        config.ReadFrom.Configuration(builder.Configuration)
              .WriteTo.Console()
              .WriteTo.File(
                  "logs/api-.log",
                  rollingInterval: RollingInterval.Day,
                  retainedFileCountLimit: 30);

        if (debugLogs)
            config.MinimumLevel.Debug()
                  .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                  .MinimumLevel.Override("System", LogEventLevel.Warning);
    });

    // Pin the key ring to a fixed, CWD-relative path (mirrors "logs/api-.log" above) instead of
    // relying on ASP.NET Core's implicit default, which resolves against the OS user profile.
    // A container running as a non-root user with no profile falls back to an in-memory key
    // ring there, silently invalidating every auth cookie on each restart.
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("keys"));

    var isDemoMode = builder.Configuration.GetValue<bool>("DEMO_MODE");

    builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");

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

    var jwtSecret = builder.Configuration["Jwt:Secret"]
        ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
    var jwtSecretBytes = Encoding.UTF8.GetBytes(jwtSecret);
    if (jwtSecretBytes.Length < 32)
        throw new InvalidOperationException("Jwt:Secret must be at least 32 bytes.");

    builder.Services.AddSingleton(builder.Configuration.GetSection("TyrePressure").Get<TyrePressureThresholds>()
        ?? TyrePressureThresholds.Default);

    builder.Services.AddMemoryCache();
    builder.Services.AddScoped<ChargingStationService>();
    builder.Services.AddScoped<PoiService>();
    builder.Services.AddSingleton<VehicleCommandGate>();

    builder.Services.AddSignalR();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    // SignalR WebSocket connections send the token via query string
                    if (ctx.Request.Path.StartsWithSegments("/hubs/telemetry"))
                    {
                        var qs = ctx.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrEmpty(qs))
                        {
                            ctx.Token = qs;
                            return Task.CompletedTask;
                        }
                    }
                    if (ctx.Request.Cookies.TryGetValue(AuthEndpoints.CookieName, out var cookie))
                        ctx.Token = cookie;
                    return Task.CompletedTask;
                },
                OnTokenValidated = async ctx =>
                {
                    // Checked after signature/lifetime validation already passed, so this only
                    // needs to catch tokens explicitly revoked via /api/auth/logout.
                    var jti = ctx.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                    if (string.IsNullOrEmpty(jti)) return;

                    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var isRevoked = await TokenRevocation.IsRevokedAsync(db, jti, ctx.HttpContext.RequestAborted);
                    if (isRevoked) ctx.Fail("Token has been revoked");
                },
            };
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(jwtSecretBytes),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddRateLimiter(opts =>
    {
        opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        opts.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ip,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 120,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });

        // Tighter, endpoint-specific limit on login to slow down credential-stuffing attempts.
        // Composes with (i.e. is enforced in addition to) the global limiter above.
        opts.AddPolicy("login", httpContext =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ip,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(5),
                    PermitLimit = 10,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });

        // Tighter limit on the widget endpoint to slow down guessing WIDGET_API_KEY, which the
        // global limiter alone (120/min) would allow at a much higher rate. Still generous
        // enough for a handful of dashboard widgets behind the same NAT polling every 30s.
        opts.AddPolicy("widget", httpContext =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ip,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(5),
                    PermitLimit = 60,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    });

    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(p =>
        {
            if (builder.Environment.IsDevelopment())
                p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            else
                p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
                 .AllowAnyHeader()
                 .AllowAnyMethod()
                 .AllowCredentials();
        }));

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (isDemoMode)
        {
            db.Database.EnsureCreated();
            if (!db.Vehicles.Any())
            {
                db.Vehicles.Add(DemoVehicleRepository.DemoVehicle);
                await db.SaveChangesAsync();
            }
            if (!db.MaintenanceItems.Any())
            {
                var vehicleId = DemoVehicleRepository.DemoVehicle.Id;
                var oilChange = new MaintenanceItem
                {
                    VehicleId = vehicleId,
                    Name = "Oil change",
                    IntervalKm = 15_000,
                    IntervalMonths = 12,
                    LastServiceDate = DateTime.UtcNow.AddMonths(-7),
                    LastServiceOdometerKm = 15_000,
                };
                var tyreRotation = new MaintenanceItem
                {
                    VehicleId = vehicleId,
                    Name = "Tyre rotation",
                    IntervalKm = 10_000,
                    LastServiceDate = DateTime.UtcNow.AddMonths(-4),
                    LastServiceOdometerKm = 15_500,
                };
                var majorService = new MaintenanceItem
                {
                    VehicleId = vehicleId,
                    Name = "Major service",
                    IntervalKm = 60_000,
                    IntervalMonths = 60,
                    LastServiceDate = DateTime.UtcNow.AddMonths(-61),
                    LastServiceOdometerKm = 0,
                };
                var cabinFilter = new MaintenanceItem
                {
                    VehicleId = vehicleId,
                    Name = "Cabin air filter",
                    IntervalKm = 15_000,
                };

                db.MaintenanceItems.AddRange(oilChange, tyreRotation, majorService, cabinFilter);
                await db.SaveChangesAsync();

                db.MaintenanceLogEntries.AddRange(
                    new MaintenanceLogEntry
                    {
                        MaintenanceItemId = oilChange.Id,
                        PerformedAt = oilChange.LastServiceDate!.Value,
                        OdometerKm = oilChange.LastServiceOdometerKm,
                    },
                    new MaintenanceLogEntry
                    {
                        MaintenanceItemId = tyreRotation.Id,
                        PerformedAt = tyreRotation.LastServiceDate!.Value,
                        OdometerKm = tyreRotation.LastServiceOdometerKm,
                    },
                    new MaintenanceLogEntry
                    {
                        MaintenanceItemId = majorService.Id,
                        PerformedAt = majorService.LastServiceDate!.Value,
                        OdometerKm = majorService.LastServiceOdometerKm,
                    });
                await db.SaveChangesAsync();
            }
        }
        else
        {
            await db.Database.MigrateAsync();
        }
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
            Log.Warning("ForwardedHeaders:TrustedProxies is not configured — trusting all RFC 1918 ranges. " +
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
    var allowedOrigins = app.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
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

    app.Use(async (ctx, next) =>
    {
        if (HttpMethods.IsPost(ctx.Request.Method) ||
            HttpMethods.IsPut(ctx.Request.Method) ||
            HttpMethods.IsPatch(ctx.Request.Method) ||
            HttpMethods.IsDelete(ctx.Request.Method))
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) && !app.Environment.IsDevelopment())
            {
                var csrfAllowed = app.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
                if (!CsrfPolicy.IsOriginAllowed(origin, csrfAllowed))
                {
                    Log.Warning(
                        "CSRF origin check failed: request Origin '{Origin}' not in allowed list ({Allowed}). " +
                        "If you are accessing from a LAN device, set CORS_ORIGIN to match the address in your browser.",
                        origin, string.Join(", ", csrfAllowed));
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(
                        "{\"error\":\"Origin not allowed. Set CORS_ORIGIN to the address you use to reach the app.\"}");
                    return;
                }
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
            .SortOperationsByMethod()
            .AddPreferredSecuritySchemes(["Bearer"])
            .AddHttpAuthentication("Bearer", _ => { }));
    }

    app.MapHealthEndpoints();
    app.MapHub<TelemetryHub>("/hubs/telemetry").RequireAuthorization();
    app.MapAuthEndpoints();
    app.MapVehicleEndpoints();
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
