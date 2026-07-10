using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Worker.Services;

public class MaintenanceCheckService(
    ILogger<MaintenanceCheckService> logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _lastNotified = new();
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6);
    private readonly TimeSpan _cooldown = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Maintenance check service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndNotifyAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error during maintenance check");
            }

            // Delay at the end (not the start) so a fresh restart checks immediately rather
            // than waiting a full interval before the first reminder can go out.
            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckAndNotifyAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var vehicles = await db.Vehicles.ToListAsync(ct);

        foreach (var vehicle in vehicles)
        {
            var items = await db.MaintenanceItems.AsNoTracking()
                .Where(m => m.VehicleId == vehicle.Id)
                .ToListAsync(ct);
            if (items.Count == 0) continue;

            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);

            foreach (var item in items)
            {
                var result = MaintenanceDueCalculator.Calculate(
                    item.IntervalKm, item.IntervalMonths,
                    item.LastServiceDate, item.LastServiceOdometerKm,
                    snapshot?.OdometerKm, DateTime.UtcNow);

                var alert = BuildAlert(item, result);
                if (alert is null) continue;

                var notifKey = $"{vehicle.Vin}/{alert.Value.Category}";
                if (_lastNotified.TryGetValue(notifKey, out var last) && DateTime.UtcNow - last < _cooldown)
                    continue;

                // Cross-service dedup: check DB so a restart doesn't forget the in-memory cooldown.
                var cutoff = DateTime.UtcNow - _cooldown;
                if (await db.AppNotifications.AnyAsync(n => n.Category == alert.Value.Category && n.VehicleId == vehicle.Id && n.CreatedAt > cutoff, ct))
                {
                    _lastNotified[notifKey] = DateTime.UtcNow;
                    continue;
                }

                _lastNotified[notifKey] = DateTime.UtcNow;
                await pushSender.SendToAllAsync(alert.Value.Title, alert.Value.Body, ct, alert.Value.Category, vehicle.Id);
                logger.LogInformation("Maintenance push sent: {Key} - {Title}", notifKey, alert.Value.Title);
            }
        }
    }

    internal readonly record struct MaintenanceAlert(string Category, string Title, string Body);

    // Category includes the item id: unlike PushNotificationCheckService's fixed category
    // strings (one alert type per vehicle), maintenance items multiply per vehicle, so a fixed
    // category would let one item's recent notification wrongly suppress another item's alert.
    internal static MaintenanceAlert? BuildAlert(MaintenanceItem item, MaintenanceDueResult result) => result.Status switch
    {
        MaintenanceDueStatus.Overdue => new MaintenanceAlert($"maintenance-overdue-{item.Id}", "Maintenance Overdue", $"{item.Name} is overdue"),
        MaintenanceDueStatus.DueSoon => new MaintenanceAlert($"maintenance-due-soon-{item.Id}", "Maintenance Due Soon", $"{item.Name} is due soon"),
        _ => null,
    };
}
