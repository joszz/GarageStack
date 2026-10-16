using System.Globalization;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Worker.Services;

namespace GarageStack.Tests;

public class TyreTrendTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);

    // Fourteen days of all four tyres, each day's pressures from the given functions of the day.
    private static List<DailyStat> Days(Func<int, double> frontLeft, Func<int, double> others, int count = 14) =>
        [.. Enumerable.Range(0, count).Select(i => new DailyStat(
            Start.AddDays(i), null, null, frontLeft(i), others(i), others(i), others(i), null, null))];

    [Fact]
    public void ATyreLosingAirTheOthersKeep_FallsBehindThem()
    {
        // Front left loses 0.02 bar a day; the others hold 2.4.
        var changes = TyreTrend.Measure(Days(i => 2.4 - 0.02 * i, _ => 2.4));

        Assert.NotNull(changes);
        Assert.InRange(changes.FrontLeft, -0.23, -0.21);
        Assert.InRange(changes.FrontRight, 0.06, 0.08);
        Assert.Equal([TyrePosition.FrontLeft], changes.Losing(0.15));
    }

    // A cold spell lowers all four tyres together: no tyre falls behind the others.
    [Fact]
    public void AllFourFallingTogether_IsTheWeather_NotAPuncture()
    {
        var changes = TyreTrend.Measure(Days(i => 2.5 - 0.03 * i, i => 2.5 - 0.03 * i));

        Assert.NotNull(changes);
        Assert.Empty(changes.Losing(0.15));
        Assert.Equal(0, changes.FrontLeft, 3);
    }

    [Fact]
    public void FewerThanAWeekOfCompleteDays_IsNoTrend()
    {
        var days = Days(i => 2.4 - 0.05 * i, _ => 2.4, count: 6);
        // A day missing one tyre does not count either.
        days.Add(new DailyStat(Start.AddDays(10), null, null, 1.9, 2.4, 2.4, null, null, null));

        Assert.Null(TyreTrend.Measure(days));
    }

    [Fact]
    public void ADropOfZero_SwitchesTheAlertOff()
    {
        var changes = TyreTrend.Measure(Days(i => 2.4 - 0.05 * i, _ => 2.4))!;

        Assert.Empty(changes.Losing(0));
    }

    [Fact]
    public void TheAlert_NamesTheTyreAndHowMuchItLost()
    {
        var changes = TyreTrend.Measure(Days(i => 2.4 - 0.02 * i, _ => 2.4));
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        TyreTrendCheckService.TyreAlert? alert;
        try
        {
            alert = TyreTrendCheckService.BuildAlert(changes, 0.15, WorkerLocalizer.Notifications());
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        Assert.NotNull(alert);
        Assert.Equal("Possible slow puncture", alert.Value.Title);
        Assert.Contains("Front left", alert.Value.Body);
        Assert.Contains("0.22", alert.Value.Body);
        Assert.Contains("14 days", alert.Value.Body);
    }

    [Fact]
    public void NoAlert_WhenEveryTyreKeepsUp()
    {
        Assert.Null(TyreTrendCheckService.BuildAlert(
            TyreTrend.Measure(Days(_ => 2.4, _ => 2.4)), 0.15, WorkerLocalizer.Notifications()));
        Assert.Null(TyreTrendCheckService.BuildAlert(null, 0.15, WorkerLocalizer.Notifications()));
    }
}
