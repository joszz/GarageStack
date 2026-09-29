using GarageStack.Api.Endpoints;

namespace GarageStack.Tests;

// Each refusal names the rule that failed: the frontend translates that code for the user.
public class MaintenanceValidationTests
{
    private static string? ItemRule(string name, string? notes, double? intervalKm, int? intervalMonths) =>
        MaintenanceEndpoints.ValidateItem(name, notes, intervalKm, intervalMonths)?.Code;

    private static string? LogEntryRule(DateTime performedAt, double? odometerKm) =>
        MaintenanceEndpoints.ValidateLogEntry(performedAt, odometerKm)?.Code;

    // ── ValidateItem: name ───────────────────────────────────────────────────
    [Fact]
    public void ValidateItem_ValidNameAndKmInterval_ReturnsNull()
    {
        Assert.Null(ItemRule("Oil change", null, 10_000, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateItem_MissingOrWhitespaceName_ReturnsError(string? name)
    {
        Assert.Equal("maintenance.nameRequired", ItemRule(name!, null, 10_000, null));
    }

    [Fact]
    public void ValidateItem_NameOver200Chars_ReturnsError()
    {
        Assert.Equal("maintenance.nameTooLong", ItemRule(new string('a', 201), null, 10_000, null));
    }

    [Fact]
    public void ValidateItem_NameExactly200Chars_ReturnsNull()
    {
        Assert.Null(ItemRule(new string('a', 200), null, 10_000, null));
    }

    // ── ValidateItem: intervals ──────────────────────────────────────────────
    [Fact]
    public void ValidateItem_OnlyKmIntervalSet_ReturnsNull()
    {
        Assert.Null(ItemRule("Tyre rotation", null, 10_000, null));
    }

    [Fact]
    public void ValidateItem_OnlyMonthsIntervalSet_ReturnsNull()
    {
        Assert.Null(ItemRule("Annual inspection", null, null, 12));
    }

    [Fact]
    public void ValidateItem_BothIntervalsSet_ReturnsNull()
    {
        Assert.Null(ItemRule("Oil change", null, 10_000, 12));
    }

    [Fact]
    public void ValidateItem_NeitherIntervalSet_ReturnsError()
    {
        Assert.Equal("maintenance.intervalRequired", ItemRule("Oil change", null, null, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void ValidateItem_KmIntervalOutOfRange_ReturnsError(double intervalKm)
    {
        Assert.Equal("maintenance.intervalKmOutOfRange", ItemRule("Oil change", null, intervalKm, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void ValidateItem_MonthsIntervalOutOfRange_ReturnsError(int intervalMonths)
    {
        Assert.Equal("maintenance.intervalMonthsOutOfRange", ItemRule("Oil change", null, null, intervalMonths));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public void ValidateItem_KmIntervalAtBounds_ReturnsNull(double intervalKm)
    {
        Assert.Null(ItemRule("Oil change", null, intervalKm, null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    public void ValidateItem_MonthsIntervalAtBounds_ReturnsNull(int intervalMonths)
    {
        Assert.Null(ItemRule("Oil change", null, null, intervalMonths));
    }

    // ── ValidateItem: notes ──────────────────────────────────────────────────
    [Fact]
    public void ValidateItem_NotesOver1000Chars_ReturnsError()
    {
        Assert.Equal("maintenance.notesTooLong", ItemRule("Oil change", new string('a', 1001), 10_000, null));
    }

    [Fact]
    public void ValidateItem_NotesExactly1000Chars_ReturnsNull()
    {
        Assert.Null(ItemRule("Oil change", new string('a', 1000), 10_000, null));
    }

    // ── ValidateLogEntry ──────────────────────────────────────────────────────
    [Fact]
    public void ValidateLogEntry_TodayNoOdometer_ReturnsNull()
    {
        Assert.Null(LogEntryRule(DateTime.UtcNow, null));
    }

    [Fact]
    public void ValidateLogEntry_ValidOdometer_ReturnsNull()
    {
        Assert.Null(LogEntryRule(DateTime.UtcNow, 12_345));
    }

    [Fact]
    public void ValidateLogEntry_FarInFuture_ReturnsError()
    {
        Assert.Equal("maintenance.serviceDateInFuture", LogEntryRule(DateTime.UtcNow.AddDays(5), null));
    }

    [Fact]
    public void ValidateLogEntry_NegativeOdometer_ReturnsError()
    {
        Assert.Equal("maintenance.odometerNegative", LogEntryRule(DateTime.UtcNow, -1));
    }

    [Fact]
    public void ValidateLogEntry_ZeroOdometer_ReturnsNull()
    {
        Assert.Null(LogEntryRule(DateTime.UtcNow, 0));
    }
}
