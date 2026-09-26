using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Services;

public class MaintenanceCheckService(
    ILogger<MaintenanceCheckService> logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender,
    IStringLocalizer<NotificationStrings> strings) : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6);
    private readonly NotificationCooldownGate _cooldownGate = new(TimeSpan.FromDays(7));

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
        var vehicleRepo = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();

        var vehicles = await vehicleRepo.GetAllAsync(ct);

        // One query for every vehicle's items instead of one query per vehicle (mirrors the
        // grouped-lookup pattern PoiPreCachingService already uses for latest-location data).
        var itemsByVehicle = (await db.MaintenanceItems.AsNoTracking().ToListAsync(ct))
            .GroupBy(m => m.VehicleId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var vehicle in vehicles)
        {
            if (!itemsByVehicle.TryGetValue(vehicle.Id, out var items) || items.Count == 0) continue;

            var odometerKm = await telemetry.GetOdometerAtAsync(vehicle.Id, DateTime.UtcNow, ct);

            foreach (var item in items)
            {
                var result = MaintenanceDueCalculator.Calculate(
                    item.IntervalKm, item.IntervalMonths,
                    item.LastServiceDate, item.LastServiceOdometerKm,
                    odometerKm, DateTime.UtcNow);

                var alert = BuildAlert(item, result, strings);
                if (alert is null) continue;

                var shouldNotify = await _cooldownGate.ShouldNotifyAsync(vehicle.Vin, alert.Value.Category, cutoff =>
                    db.WasNotificationSentSinceAsync(alert.Value.Category, vehicle.Id, cutoff, ct));
                if (!shouldNotify) continue;

                await pushSender.SendToAllAsync(alert.Value.Title, alert.Value.Body, ct, alert.Value.Category, vehicle.Id);
                logger.LogInformation("Maintenance push sent: {Vin}/{Category} - {Title}",
                    LogRedaction.Vin(vehicle.Vin), alert.Value.Category, alert.Value.Title);
            }
        }
    }

    internal readonly record struct MaintenanceAlert(string Category, string Title, string Body);

    // Category includes the item id: unlike PushNotificationCheckService's fixed category
    // strings (one alert type per vehicle), maintenance items multiply per vehicle, so a fixed
    // category would let one item's recent notification wrongly suppress another item's alert.
    internal static MaintenanceAlert? BuildAlert(MaintenanceItem item, MaintenanceDueResult result, IStringLocalizer<NotificationStrings> strings) =>
        result.Status switch
        {
            MaintenanceDueStatus.Overdue => new MaintenanceAlert(
                NotificationCategories.MaintenanceOverdue(item.Id),
                strings["MaintenanceOverdueTitle"],
                strings["MaintenanceOverdueBody", item.Name]),
            MaintenanceDueStatus.DueSoon => new MaintenanceAlert(
                NotificationCategories.MaintenanceDueSoon(item.Id),
                strings["MaintenanceDueSoonTitle"],
                strings["MaintenanceDueSoonBody", item.Name]),
            _ => null,
        };
}
