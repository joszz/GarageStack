using GarageStack.Data;
using GarageStack.Data.Extensions;
using GarageStack.Worker.Mqtt;
using GarageStack.Worker.Services;
using Microsoft.EntityFrameworkCore;
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

    builder.Services.AddGarageStackData(connectionString);
    builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));
    builder.Services.AddSingleton<GarageStack.Core.Interfaces.IPushSender, GarageStack.Worker.Services.PushSenderService>();
    builder.Services.AddHostedService<MqttConsumerService>();
    builder.Services.AddHostedService<PushNotificationCheckService>();

    var host = builder.Build();

    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    host.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
