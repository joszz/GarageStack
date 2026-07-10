using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class MaintenanceDueCalculatorTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);

    // ── km-only interval ─────────────────────────────────────────────────────
    [Fact]
    public void KmOnly_FarFromDue_ReturnsOk()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: null,
            lastServiceDate: null, lastServiceOdometerKm: 10_000,
            currentOdometerKm: 15_000, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Ok, result.Status);
    }

    [Fact]
    public void KmOnly_At90PercentElapsed_ReturnsDueSoon()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: null,
            lastServiceDate: null, lastServiceOdometerKm: 0,
            currentOdometerKm: 9_000, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.DueSoon, result.Status);
    }

    [Fact]
    public void KmOnly_JustBelow90Percent_ReturnsOk()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: null,
            lastServiceDate: null, lastServiceOdometerKm: 0,
            currentOdometerKm: 8_999, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Ok, result.Status);
    }

    [Fact]
    public void KmOnly_AtOrPastInterval_ReturnsOverdue()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: null,
            lastServiceDate: null, lastServiceOdometerKm: 0,
            currentOdometerKm: 10_500, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Overdue, result.Status);
        Assert.Equal(10_000, result.NextDueOdometerKm);
        Assert.Equal(-500, result.KmRemaining);
    }

    // ── months-only interval ─────────────────────────────────────────────────
    [Fact]
    public void MonthsOnly_FarFromDue_ReturnsOk()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: null, intervalMonths: 12,
            lastServiceDate: Now.AddMonths(-1), lastServiceOdometerKm: null,
            currentOdometerKm: null, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Ok, result.Status);
    }

    [Fact]
    public void MonthsOnly_NearEndOfInterval_ReturnsDueSoon()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: null, intervalMonths: 12,
            lastServiceDate: Now.AddMonths(-11), lastServiceOdometerKm: null,
            currentOdometerKm: null, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.DueSoon, result.Status);
    }

    [Fact]
    public void MonthsOnly_PastInterval_ReturnsOverdue()
    {
        var lastServiceDate = Now.AddMonths(-13);
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: null, intervalMonths: 12,
            lastServiceDate: lastServiceDate, lastServiceOdometerKm: null,
            currentOdometerKm: null, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Overdue, result.Status);
        Assert.Equal(lastServiceDate.AddMonths(12), result.NextDueDate);
    }

    // ── both intervals set: "whichever comes first" ─────────────────────────
    [Fact]
    public void BothSet_KmOverdueDateOk_OverallOverdue()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: 12,
            lastServiceDate: Now.AddMonths(-1), lastServiceOdometerKm: 0,
            currentOdometerKm: 10_500, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Overdue, result.Status);
    }

    [Fact]
    public void BothSet_DateOverdueKmOk_OverallOverdue()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: 12,
            lastServiceDate: Now.AddMonths(-13), lastServiceOdometerKm: 0,
            currentOdometerKm: 1_000, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Overdue, result.Status);
    }

    [Fact]
    public void BothSet_BothDueSoon_OverallDueSoon()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: 12,
            lastServiceDate: Now.AddMonths(-11), lastServiceOdometerKm: 0,
            currentOdometerKm: 9_000, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.DueSoon, result.Status);
    }

    [Fact]
    public void BothSet_BothOk_OverallOk()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: 12,
            lastServiceDate: Now.AddMonths(-1), lastServiceOdometerKm: 0,
            currentOdometerKm: 1_000, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Ok, result.Status);
    }

    // ── unknown / partial-data fallback ──────────────────────────────────────
    [Fact]
    public void NoBaselineAtAll_ReturnsUnknown()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: 12,
            lastServiceDate: null, lastServiceOdometerKm: null,
            currentOdometerKm: 15_000, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Unknown, result.Status);
    }

    [Fact]
    public void OnlyKmIntervalSet_NoMonthsInterval_ResolvesFromKmAloneNotUnknown()
    {
        // No IntervalMonths at all (item was created km-only) - the date dimension is
        // inapplicable by design, not "missing data", and must not drag the result to Unknown.
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: null,
            lastServiceDate: null, lastServiceOdometerKm: 0,
            currentOdometerKm: 10_500, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Overdue, result.Status);
    }

    [Fact]
    public void IntervalKmSetButLastServiceOdometerMissing_FallsBackToDateDimension()
    {
        // Item has a km interval configured, but was only ever logged by date (no odometer
        // recorded) - km dimension can't be evaluated, must fall back cleanly to the date
        // dimension instead of resolving Unknown.
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: 12,
            lastServiceDate: Now.AddMonths(-13), lastServiceOdometerKm: null,
            currentOdometerKm: 5_000, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Overdue, result.Status);
        Assert.Null(result.NextDueOdometerKm);
    }

    [Fact]
    public void CurrentOdometerUnavailable_FallsBackToDateDimension()
    {
        // Vehicle has never reported telemetry yet - km dimension can't be evaluated even
        // though the item has both a km interval and a recorded baseline odometer.
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 10_000, intervalMonths: 12,
            lastServiceDate: Now.AddMonths(-1), lastServiceOdometerKm: 0,
            currentOdometerKm: null, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Ok, result.Status);
    }

    [Fact]
    public void BothDimensionsInapplicableForDifferentReasons_ReturnsUnknown()
    {
        // Km dimension: no interval set. Date dimension: interval set but no baseline date.
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: null, intervalMonths: 12,
            lastServiceDate: null, lastServiceOdometerKm: 5_000,
            currentOdometerKm: 10_000, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Unknown, result.Status);
    }

    [Fact]
    public void DeeplyOverdue_ReturnsOverdueWithoutError()
    {
        var result = MaintenanceDueCalculator.Calculate(
            intervalKm: 5_000, intervalMonths: 6,
            lastServiceDate: Now.AddYears(-5), lastServiceOdometerKm: 0,
            currentOdometerKm: 200_000, nowUtc: Now);

        Assert.Equal(MaintenanceDueStatus.Overdue, result.Status);
    }
}
