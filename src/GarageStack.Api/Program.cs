using System.Globalization;
using System.Text.Json.Serialization;
using GarageStack.Api;
using GarageStack.Api.Endpoints;
using GarageStack.Api.Services;
using GarageStack.Core.Interfaces;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is not configured.");

    builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");
    builder.Services.AddGarageStackData(connectionString);
    builder.Services.AddSingleton<MqttPublisher>();
    builder.Services.AddSingleton<IMqttPublisher>(sp => sp.GetRequiredService<MqttPublisher>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttPublisher>());
    builder.Services.AddOpenApi();
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

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    if (ctx.Request.Cookies.TryGetValue("garagestack-auth", out var cookie))
                        ctx.Token = cookie;
                    return Task.CompletedTask;
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

    // Defense-in-depth: when an Origin header is present on a state-changing request,
    // verify it matches a configured allowed origin. SameSite=Strict is the primary CSRF
    // protection; this adds an explicit server-side check for deployments where that alone
    // is not sufficient (e.g., same-site subdomain compromise).
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
                var allowed = app.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
                if (!CsrfPolicy.IsOriginAllowed(origin, allowed))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
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
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(opts => opts.WithTitle("GarageStack API"));
    }

    app.MapAuthEndpoints();
    app.MapVehicleEndpoints();
    app.MapNotificationEndpoints();
    app.MapWidgetEndpoints();

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
