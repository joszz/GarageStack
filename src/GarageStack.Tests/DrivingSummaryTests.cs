using System.Globalization;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Worker.Services;

namespace GarageStack.Tests;

public class DrivingSummaryTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    private static TripSummaryDto Trip(double km) =>
        new(0, 1, DateTime.UtcNow, DateTime.UtcNow, km, 10, null, null, null, null, 0);

    private static DailyStat Day(int offset, double? counter, double? km, double? since = null, bool unbacked = false) =>
        new(Monday.AddDays(offset), null, null, null, null, null, null, null, counter, since, unbacked, km);

    [Fact]
    public void AHybridsWeek_IsItsTripsCountedDistanceAndFuel()
    {
        var figures = DrivingSummary.Build(
            [Trip(40), Trip(38), Trip(20)],
            [Day(-7, 360, 60), Day(-6, 0, 0), Day(-5, 252, 42)],
            "hev");

        Assert.Equal(new DrivingSummaryFigures(3, 102, 6.12, null, 6), figures);
    }

    [Fact]
    public void ABevsEnergy_TakesTheSinceChargeCounter_OnADayTheDayCounterRanPastIt()
    {
        var figures = DrivingSummary.Build(
            [Trip(30)], [Day(-7, 4.5, 30), Day(-6, 134, 4, since: 0.6, unbacked: true)], "bev");

        Assert.Equal(5.1, figures.EnergyKwh!.Value, 6);
        Assert.Equal(15, figures.PerHundredKm!.Value, 6);
        Assert.Null(figures.FuelLitres);
    }

    [Fact]
    public void APlugInHybrid_GetsNoEnergy_AndAPeriodWithoutCountedDistance_TheTrips()
    {
        var figures = DrivingSummary.Build([Trip(12.5), Trip(7.5)], [Day(-7, 9, null)], "phev");

        Assert.Equal(new DrivingSummaryFigures(2, 20, null, null, null), figures);
        Assert.True(figures.HasDriving);
        Assert.False(DrivingSummary.Build([], [Day(-7, 0, 0)], "hev").HasDriving);
    }

    [Fact]
    public void TooLittleDriving_HasNoConsumption()
    {
        Assert.Null(DrivingSummary.Build([Trip(3)], [Day(-7, 20, 3)], "hev").PerHundredKm);
    }
}

public class DrivingSummaryServiceTests
{
    private static readonly HashSet<string> Both = [NotificationCategories.WeeklySummary, NotificationCategories.MonthlySummary];

    [Fact]
    public void TheWeek_IsDueOnMondayMorning_ForTheMondayToSundayBefore()
    {
        var due = DrivingSummaryService.Due(new DateTime(2026, 11, 23, 8, 30, 0), Both);

        Assert.Equal([new DrivingSummaryService.DueSummary("weekly-summary", new(2026, 11, 16), new(2026, 11, 22))], due);
        Assert.Empty(DrivingSummaryService.Due(new DateTime(2026, 11, 23, 7, 59, 0), Both));
        Assert.Empty(DrivingSummaryService.Due(new DateTime(2026, 11, 24, 9, 0, 0), Both));
    }

    [Fact]
    public void TheMonth_IsDueOnTheFirst_AndNeitherWithoutADeviceThatWantsIt()
    {
        var first = new DateTime(2026, 12, 1, 10, 0, 0);

        Assert.Equal(
            [new DrivingSummaryService.DueSummary("monthly-summary", new(2026, 11, 1), new(2026, 11, 30))],
            DrivingSummaryService.Due(first, Both));
        Assert.Empty(DrivingSummaryService.Due(first, new HashSet<string> { NotificationCategories.WeeklySummary }));
        Assert.Empty(DrivingSummaryService.Due(new DateTime(2026, 11, 23, 9, 0, 0), new HashSet<string>()));
    }

    [Fact]
    public void TheNotification_SaysTheDistanceTripsAndFuel_InTheNotificationLanguage()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            var strings = WorkerLocalizer.Notifications();
            var week = new DrivingSummaryService.DueSummary("weekly-summary", new(2026, 11, 16), new(2026, 11, 22));
            var month = new DrivingSummaryService.DueSummary("monthly-summary", new(2026, 10, 1), new(2026, 10, 31));
            var figures = new DrivingSummaryFigures(12, 1312.4, 81.26, null, 6.19);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Assert.Equal(("Your week: 1,312 km", "12 trips · 81.3 L of fuel · 6.2 L/100 km"),
                DrivingSummaryService.Compose(week, figures, strings));
            Assert.Equal("1 trip · 4.5 kWh",
                DrivingSummaryService.Compose(week, new DrivingSummaryFigures(1, 30, null, 4.5, null), strings).Body);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nl");
            Assert.Equal(("Jouw oktober: 1.312 km", "12 ritten · 81,3 L brandstof · 6,2 L/100 km"),
                DrivingSummaryService.Compose(month, figures, strings));
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }
}
