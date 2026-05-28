using GarageStack.Core.Interfaces;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GarageStack.Worker.Services;

public class PushNotificationCheckService(
    ILogger<PushNotificationCheckService> logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _lastNotified = new();
    private readonly TimeSpan _cooldown = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Push notification check service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            try
            {
                await CheckAndNotifyAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error during push notification check");
            }
        }
    }

    private async Task CheckAndNotifyAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vehicles = await db.Vehicles.ToListAsync(ct);

        foreach (var vehicle in vehicles)
        {
            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            if (snapshot is null) continue;

            var alerts = new List<(string key, string title, string body)>();
            CheckTyrePressure(snapshot, alerts);
            CheckEvSoc(snapshot, alerts);

            foreach (var (key, title, body) in alerts)
            {
                var notifKey = $"{vehicle.Vin}/{key}";
                if (_lastNotified.TryGetValue(notifKey, out var last) && DateTime.UtcNow - last < _cooldown)
                    continue;

                _lastNotified[notifKey] = DateTime.UtcNow;
                await pushSender.SendToAllAsync(title, body, ct);
                logger.LogInformation("Push sent: {Key} - {Title}", notifKey, title);
            }
        }
    }

    private static void CheckTyrePressure(Core.Models.TelemetrySnapshot s, List<(string, string, string)> alerts)
    {
        var low = new List<string>();
        if (s.TyrePressureFrontLeft is not null && s.TyrePressureFrontLeft < 2.2) low.Add("FL");
        if (s.TyrePressureFrontRight is not null && s.TyrePressureFrontRight < 2.2) low.Add("FR");
        if (s.TyrePressureRearLeft is not null && s.TyrePressureRearLeft < 2.2) low.Add("RL");
        if (s.TyrePressureRearRight is not null && s.TyrePressureRearRight < 2.2) low.Add("RR");

        if (low.Count > 0)
            alerts.Add(("low-tyre", "Low Tyre Pressure", $"Tyre pressure low: {string.Join(", ", low)}"));
    }

    private static void CheckEvSoc(Core.Models.TelemetrySnapshot s, List<(string, string, string)> alerts)
    {
        if (s.EvSocPercent is not null && s.EvSocPercent < 20)
            alerts.Add(("low-ev", "Low EV Battery", $"EV battery at {s.EvSocPercent:F0}%"));
    }
}
