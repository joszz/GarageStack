using System.Globalization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Services;

/// <summary>
/// Sends what each car did last week, on Monday morning, and last month, on the first of the month:
/// trips, distance, and what it used. Both are opt-in per device
/// (<see cref="NotificationCategories.OptInGroups"/>): while no device has switched one on, it is
/// not even worked out, so it never fills the notification list unasked either. Days are the
/// server's (TZ), as the other Worker checks count them. A period without driving sends nothing.
/// </summary>
public class DrivingSummaryService(
    ILogger<DrivingSummaryService> logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender,
    VehicleDefaults defaults,
    IStringLocalizer<NotificationStrings> strings,
    TimeProvider time) : PeriodicBackgroundService(logger)
{
    /// <summary>The hour of the morning the summaries go out from, local time.</summary>
    internal const int SendHour = 8;

    protected override string Name => "Driving summary";

    protected override TimeSpan InitialDelay => TimeSpan.FromMinutes(5);

    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);

    /// <summary>A summary that is due: its category and the local days it covers, both ends included.</summary>
    internal readonly record struct DueSummary(string Category, DateOnly From, DateOnly To);

    /// <summary>
    /// The summaries due at <paramref name="local"/> among the ones some device wants: the past week
    /// on a Monday from <see cref="SendHour"/>, the past month on the first. On that day only, so a
    /// Worker that was down over the weekend does not send last week's on a Thursday.
    /// </summary>
    internal static IReadOnlyList<DueSummary> Due(DateTime local, IReadOnlySet<string> wanted)
    {
        var today = DateOnly.FromDateTime(local);
        if (local.Hour < SendHour) return [];
        var due = new List<DueSummary>(2);
        if (wanted.Contains(NotificationCategories.WeeklySummary) && today.DayOfWeek == DayOfWeek.Monday)
            due.Add(new(NotificationCategories.WeeklySummary, today.AddDays(-7), today.AddDays(-1)));
        if (wanted.Contains(NotificationCategories.MonthlySummary) && today.Day == 1)
            due.Add(new(NotificationCategories.MonthlySummary, today.AddMonths(-1), today.AddDays(-1)));
        return due;
    }

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        var zone = time.LocalTimeZone;
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);

        using var scope = scopeFactory.CreateScope();
        var subscriptions = await scope.ServiceProvider.GetRequiredService<IPushSubscriptionRepository>().ListAsync(ct);
        var wanted = subscriptions.SelectMany(s => s.IncludedCategories).ToHashSet(StringComparer.Ordinal);
        var due = Due(local, wanted);
        if (due.Count == 0) return;

        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var trips = scope.ServiceProvider.GetRequiredService<ITripRepository>();
        var vehicles = await scope.ServiceProvider.GetRequiredService<IVehicleRepository>().GetAllAsync(ct);
        var today = DateOnly.FromDateTime(local);
        var sentToday = ZoneTime.StartOfDay(today, zone);

        foreach (var vehicle in vehicles)
        {
            var vehicleType = VehicleSettingsResolver.Resolve(vehicle, defaults).VehicleType;
            foreach (var summary in due)
            {
                if (await notifications.WasSentSinceAsync(summary.Category, vehicle.Id, sentToday, ct)) continue;

                var (start, end) = DailyStatsBuilder.UtcRange(summary.From, summary.To, zone);
                var figures = DrivingSummary.Build(
                    await trips.GetSummariesAsync(vehicle.Id, start, end, ct),
                    await telemetry.GetDailyStatsAsync(vehicle.Id, summary.From, summary.To, zone, today, ct),
                    vehicleType);
                if (!figures.HasDriving) continue;

                var (title, body) = Compose(summary, figures, strings);
                await pushSender.SendToAllAsync(title, body, ct, summary.Category, vehicle.Id);
                logger.LogInformation("Driving summary sent: {Vin}/{Category} - {Title}",
                    LogRedaction.Vin(vehicle.Vin), summary.Category, title);
            }
        }
    }

    /// <summary>The notification for a summary, in the notification language and its number format.</summary>
    internal static (string Title, string Body) Compose(
        DueSummary summary, DrivingSummaryFigures figures, IStringLocalizer<NotificationStrings> strings)
    {
        var culture = CultureInfo.CurrentUICulture;
        string Number(double value, int decimals) => value.ToString($"N{decimals}", culture);

        var distance = Number(figures.DistanceKm, 0);
        var title = summary.Category == NotificationCategories.WeeklySummary
            ? strings["WeeklySummaryTitle", distance]
            : strings["MonthlySummaryTitle", culture.DateTimeFormat.GetMonthName(summary.From.Month), distance];

        var parts = new List<string>(3)
        {
            figures.Trips == 1 ? strings["SummaryOneTrip"] : strings["SummaryTrips", figures.Trips.ToString(culture)],
        };
        if (figures.FuelLitres is { } litres)
        {
            parts.Add(strings["SummaryFuel", Number(litres, 1)]);
            if (figures.PerHundredKm is { } l100) parts.Add(strings["SummaryFuelConsumption", Number(l100, 1)]);
        }
        else if (figures.EnergyKwh is { } kwh)
        {
            parts.Add(strings["SummaryEnergy", Number(kwh, 1)]);
            if (figures.PerHundredKm is { } kwh100) parts.Add(strings["SummaryEnergyConsumption", Number(kwh100, 1)]);
        }

        return (title, string.Join(" · ", parts));
    }
}
