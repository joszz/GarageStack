using System.Globalization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Services;

/// <summary>
/// Warns about a slow puncture: one tyre losing air the other three keep, over the last two weeks
/// (see <see cref="TyreTrend"/>). A pressure that drifts this slowly never trips the low-tyre alert
/// until it is already low; this catches the leak while the tyre is still fine.
/// </summary>
public class TyreTrendCheckService(
    ILogger<TyreTrendCheckService> logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender,
    TyrePressureThresholds thresholds,
    IStringLocalizer<NotificationStrings> strings,
    TimeProvider time) : PeriodicBackgroundService(logger)
{
    // A trend changes over days, not hours: once warned, the driver has time to look.
    private readonly NotificationCooldownGate _cooldownGate = new(TimeSpan.FromDays(3));

    private static readonly Dictionary<TyrePosition, string> PositionKeys = new()
    {
        [TyrePosition.FrontLeft] = "TyreFrontLeft",
        [TyrePosition.FrontRight] = "TyreFrontRight",
        [TyrePosition.RearLeft] = "TyreRearLeft",
        [TyrePosition.RearRight] = "TyreRearRight",
    };

    protected override string Name => "Tyre trend check";

    protected override TimeSpan InitialDelay => TimeSpan.FromMinutes(10);

    protected override TimeSpan Interval => TimeSpan.FromHours(6);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        if (thresholds.DropBar <= 0) return;

        using var scope = scopeFactory.CreateScope();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var vehicles = await scope.ServiceProvider.GetRequiredService<IVehicleRepository>().GetAllAsync(ct);

        // Days by the server's clock: over two weeks, where a day starts makes no difference.
        var zone = time.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, zone));
        var first = today.AddDays(-(TyreTrend.WindowDays - 1));

        foreach (var vehicle in vehicles)
        {
            var days = await telemetry.GetDailyStatsAsync(vehicle.Id, first, today, zone, today, ct);
            if (BuildAlert(TyreTrend.Measure(days), thresholds.DropBar, strings) is not { } alert) continue;

            var shouldNotify = await _cooldownGate.ShouldNotifyAsync(vehicle.Vin, NotificationCategories.SlowPuncture, cutoff =>
                notifications.WasSentSinceAsync(NotificationCategories.SlowPuncture, vehicle.Id, cutoff, ct));
            if (!shouldNotify) continue;

            await pushSender.SendToAllAsync(alert.Title, alert.Body, ct, NotificationCategories.SlowPuncture, vehicle.Id);
            logger.LogInformation("Slow puncture push sent: {Vin} - {Body}", LogRedaction.Vin(vehicle.Vin), alert.Body);
        }
    }

    internal readonly record struct TyreAlert(string Title, string Body);

    /// <summary>The warning for the tyres <paramref name="changes"/> shows losing air, or null when none is.</summary>
    internal static TyreAlert? BuildAlert(TyreChanges? changes, double dropBar, IStringLocalizer<NotificationStrings> strings)
    {
        if (changes?.Losing(dropBar) is not { Count: > 0 } losing) return null;

        var worst = losing.Min(position => position switch
        {
            TyrePosition.FrontLeft => changes.FrontLeft,
            TyrePosition.FrontRight => changes.FrontRight,
            TyrePosition.RearLeft => changes.RearLeft,
            _ => changes.RearRight,
        });
        var names = string.Join(", ", losing.Select(position => strings[PositionKeys[position]].Value));
        return new TyreAlert(
            strings["SlowPunctureTitle"],
            // In the notification's language, as its words are: 0,22 in Dutch.
            strings["SlowPunctureBody", names, (-worst).ToString("F2", CultureInfo.CurrentUICulture), changes.Days]);
    }
}
