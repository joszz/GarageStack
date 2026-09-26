using System.Globalization;
using System.Text.Json.Serialization;
using GarageStack.Api;
using GarageStack.Api.Authentication;
using GarageStack.Api.Endpoints;
using GarageStack.Api.Hosting;
using GarageStack.Api.Hubs;
using GarageStack.Api.Services;
using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Data;
using GarageStack.Data.Demo;
using GarageStack.Data.Extensions;
using Microsoft.AspNetCore.DataProtection;
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
    // Every error answer is a ProblemDetails body; refusals add a `code` the browser translates.
    builder.Services.AddProblemDetails();
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
    builder.Services.AddScoped<TripPlaceService>();
    builder.Services.AddScoped<MapMatchService>();
    builder.Services.AddSingleton<VehicleCommandGate>();

    builder.Services.AddSignalR();

    builder.Services.AddGarageStackAuthentication(builder.Configuration, builder.Environment);
    builder.Services.AddGarageStackRateLimiting(builder.Configuration);

    // The browser origins allowed to call the API: CORS for cross-origin deployments and the
    // CSRF origin check below. Read once here; it is fixed for the process lifetime.
    var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    builder.Services.AddGarageStackCors(allowedOrigins, builder.Environment);

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (isDemoMode)
            await DemoSeeder.SeedAsync(db);
        else
            await db.Database.MigrateAsync();
    }

    app.UseExceptionHandler();
    app.UseGarageStackForwardedHeaders();
    app.UseSerilogRequestLogging();
    app.UseCors();

    // Rate limiting runs before the CSRF origin check so that a flood of requests with a
    // spoofed/mismatched Origin gets throttled instead of generating unbounded warning-log
    // volume in the check.
    app.UseRateLimiter();
    app.UseCsrfOriginCheck(allowedOrigins);

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
    app.MapTripEndpoints();
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
