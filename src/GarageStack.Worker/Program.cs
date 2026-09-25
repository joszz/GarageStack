using System.Globalization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Data.Extensions;
using GarageStack.Worker.Mqtt;
using GarageStack.Worker.Services;
using Serilog;

Log.Logger = HostingExtensions.CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddGarageStackSerilog(builder.Configuration, "worker");

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
    builder.Services.AddTyrePressureThresholds(builder.Configuration);
    builder.Services.AddSingleton<IPushSender, PushSenderService>();
    builder.Services.AddHostedService<MqttConsumerService>();
    builder.Services.AddHostedService<PushNotificationCheckService>();
    builder.Services.AddHostedService<MaintenanceCheckService>();
    builder.Services.AddHostedService<PoiPreCachingService>();
    builder.Services.AddHostedService<TripRecorderService>();

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
