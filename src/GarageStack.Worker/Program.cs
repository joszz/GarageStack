using System.Globalization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Data.Extensions;
using GarageStack.Worker.Mqtt;
using GarageStack.Worker.Services;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    var debugLogs = string.Equals(builder.Configuration["DEBUG_LOGS"], "true", StringComparison.OrdinalIgnoreCase);

    builder.Services.AddSerilog((_, config) =>
    {
        config.ReadFrom.Configuration(builder.Configuration)
              .WriteTo.Console()
              .WriteTo.File(
                  "logs/worker-.log",
                  rollingInterval: RollingInterval.Day,
                  retainedFileCountLimit: 30);

        if (debugLogs)
            config.MinimumLevel.Debug()
                  .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                  .MinimumLevel.Override("System", LogEventLevel.Warning);
    });

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is not configured.");

    // Push notification titles and bodies come from Resources/NotificationStrings*.resx. The
    // Worker has no browser request to take a language from, so the language is a deployment
    // setting (NOTIFICATION_LANGUAGE). Only the UI culture is changed: number and date parsing
    // of gateway payloads stays culture-invariant.
    ApplyNotificationCulture(builder.Configuration["Notifications:Culture"]);
    builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");

    builder.Services.AddGarageStackData(connectionString);
    builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));
    builder.Services.AddSingleton(builder.Configuration.GetSection("TyrePressure").Get<TyrePressureThresholds>()
        ?? TyrePressureThresholds.Default);
    builder.Services.AddSingleton<IPushSender, PushSenderService>();
    builder.Services.AddHostedService<MqttConsumerService>();
    builder.Services.AddHostedService<PushNotificationCheckService>();
    builder.Services.AddHostedService<MaintenanceCheckService>();
    builder.Services.AddHostedService<PoiPreCachingService>();

    var host = builder.Build();

    host.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

static void ApplyNotificationCulture(string? cultureName)
{
    if (string.IsNullOrWhiteSpace(cultureName))
        return;

    try
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Log.Information("Notification language: {Culture}", culture.Name);
    }
    catch (CultureNotFoundException)
    {
        Log.Warning("NOTIFICATION_LANGUAGE '{Culture}' is not a known culture; notifications stay in English", cultureName);
    }
}
