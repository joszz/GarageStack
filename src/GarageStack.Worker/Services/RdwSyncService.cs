using System.Globalization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Services;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Services;

/// <summary>
/// Keeps each car's RDW lookup current and says when something in it needs attention: a new open
/// recall (once per recall), the APK a month and a week ahead and once it has run out, and no
/// insurance on record. With RDW_ENABLED unset it stops at once and asks nothing.
/// </summary>
public class RdwSyncService(
    ILogger<RdwSyncService> logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender,
    RdwOptions options,
    IStringLocalizer<NotificationStrings> strings,
    TimeProvider time) : PeriodicBackgroundService(logger)
{
    // Each APK category names its expiry date, so this only spaces out the reminders that repeat:
    // an expired APK, and a car without insurance. A month is also as far back as the history of a
    // notification the user deleted reaches (housekeeping removes those at 30 days).
    private readonly NotificationCooldownGate _cooldownGate = new(TimeSpan.FromDays(30));

    protected override string Name => "RDW sync";

    // Out of the way of the start-up rush; the API looks a plate up itself as soon as it is entered.
    protected override TimeSpan InitialDelay => TimeSpan.FromMinutes(5);

    // A pass that is not due a lookup only reads a row per car, so looking often costs nothing and
    // a recall the API's lookup found is pushed within the hour.
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Enabled) return base.ExecuteAsync(stoppingToken);

        logger.LogInformation("RDW lookups are off (RDW_ENABLED); the RDW sync does not run");
        return Task.CompletedTask;
    }

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        if (!options.Enabled) return;

        using var scope = scopeFactory.CreateScope();
        var vehicles = await scope.ServiceProvider.GetRequiredService<IVehicleRepository>().GetAllAsync(ct);
        var rdw = scope.ServiceProvider.GetRequiredService<IRdwRepository>();
        var refresher = scope.ServiceProvider.GetRequiredService<RdwRefresher>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var lookups = (await rdw.ListAsync(ct)).ToDictionary(i => i.VehicleId);

        foreach (var vehicle in vehicles)
        {
            if (vehicle.LicensePlate is null) continue;

            var info = lookups.GetValueOrDefault(vehicle.Id);
            if (refresher.IsDue(vehicle, info))
                info = await refresher.RefreshAsync(vehicle, ct);

            if (info is not { Found: true, FetchedAt: { } fetchedAt } || info.LicensePlate != vehicle.LicensePlate) continue;

            // Only what the RDW said lately: an APK renewed while the RDW could not be reached
            // must not be reported as running out.
            var now = time.GetUtcNow().UtcDateTime;
            if (now - fetchedAt > 2 * options.RefreshInterval) continue;

            await NotifyRecallsAsync(vehicle, info, rdw, now, ct);
            foreach (var alert in BuildAlerts(info, now, strings))
            {
                if (!await _cooldownGate.ShouldNotifyAsync(vehicle.Vin, vehicle.Id, alert.Category, notifications, ct)) continue;

                await pushSender.SendToAllAsync(alert.Title, alert.Body, ct, alert.Category, vehicle.Id);
                logger.LogInformation("RDW push sent: {Vin}/{Category}", LogRedaction.Vin(vehicle.Vin), alert.Category);
            }
        }
    }

    // One push per open recall, ever: the recall is marked the moment it went out, and a recall
    // the RDW later reports as repaired, or open again, was marked already.
    private async Task NotifyRecallsAsync(Vehicle vehicle, RdwVehicleInfo info, IRdwRepository rdw, DateTime now, CancellationToken ct)
    {
        foreach (var recall in RdwRecalls.ToNotify(RdwRecalls.Parse(info.RecallsJson)))
        {
            var alert = BuildRecallAlert(recall, strings);
            await pushSender.SendToAllAsync(alert.Title, alert.Body, ct, alert.Category, vehicle.Id);
            await rdw.MarkRecallsNotifiedAsync(vehicle.Id, [recall.Code], now, ct);
            logger.LogInformation("RDW push sent: {Vin}/{Category}", LogRedaction.Vin(vehicle.Vin), alert.Category);
        }
    }

    internal readonly record struct RdwAlert(string Category, string Title, string Body);

    internal static RdwAlert BuildRecallAlert(RdwRecall recall, IStringLocalizer<NotificationStrings> strings) =>
        new(
            NotificationCategories.RdwRecall(recall.Code),
            strings["RdwRecallTitle"],
            recall.Defect is { } defect
                ? strings["RdwRecallBody", recall.Code, defect]
                : strings["RdwRecallBodyNoDetails", recall.Code]);

    /// <summary>The APK and insurance alerts the lookup calls for at <paramref name="utcNow"/>, before any cooldown.</summary>
    internal static IReadOnlyList<RdwAlert> BuildAlerts(RdwVehicleInfo info, DateTime utcNow, IStringLocalizer<NotificationStrings> strings)
    {
        var alerts = new List<RdwAlert>();

        if (info.ApkExpiresOn is { } expiresOn)
        {
            var daysLeft = ApkReminder.DaysLeft(expiresOn, utcNow);
            // In the notification's language, as its words are: "30 oktober 2026" in Dutch.
            var date = expiresOn.ToString("d MMMM yyyy", CultureInfo.CurrentUICulture);
            switch (ApkReminder.StageOf(daysLeft))
            {
                case ApkStage.Expired:
                    alerts.Add(new(NotificationCategories.RdwApkExpired(expiresOn),
                        strings["RdwApkExpiredTitle"], strings["RdwApkExpiredBody", date]));
                    break;
                case ApkStage.DueWithinWeek:
                    alerts.Add(new(NotificationCategories.RdwApkDue(ApkReminder.WeekNoticeDays, expiresOn),
                        strings["RdwApkDueTitle"], DueBody(daysLeft, date, strings)));
                    break;
                case ApkStage.DueWithinMonth:
                    alerts.Add(new(NotificationCategories.RdwApkDue(ApkReminder.MonthNoticeDays, expiresOn),
                        strings["RdwApkDueTitle"], DueBody(daysLeft, date, strings)));
                    break;
            }
        }

        if (info.WamInsured == false)
            alerts.Add(new(NotificationCategories.RdwUninsured, strings["RdwUninsuredTitle"], strings["RdwUninsuredBody"]));

        return alerts;
    }

    private static string DueBody(int daysLeft, string date, IStringLocalizer<NotificationStrings> strings) => daysLeft switch
    {
        0 => strings["RdwApkDueTodayBody", date],
        1 => strings["RdwApkDueTomorrowBody", date],
        _ => strings["RdwApkDueBody", date, daysLeft],
    };
}
