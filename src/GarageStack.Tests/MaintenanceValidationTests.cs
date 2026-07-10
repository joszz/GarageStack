using GarageStack.Api.Endpoints;

namespace GarageStack.Tests;

public class MaintenanceValidationTests
{
    // ── ValidateItem: name ───────────────────────────────────────────────────
    [Fact]
    public void ValidateItem_ValidNameAndKmInterval_ReturnsNull()
    {
        Assert.Null(MaintenanceEndpoints.ValidateItem("Oil change", null, 10_000, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateItem_MissingOrWhitespaceName_ReturnsError(string? name)
    {
        Assert.NotNull(MaintenanceEndpoints.ValidateItem(name!, null, 10_000, null));
    }

    [Fact]
    public void ValidateItem_NameOver200Chars_ReturnsError()
    {
        var name = new string('a', 201);
        Assert.NotNull(MaintenanceEndpoints.ValidateItem(name, null, 10_000, null));
    }

    [Fact]
    public void ValidateItem_NameExactly200Chars_ReturnsNull()
    {
        var name = new string('a', 200);
        Assert.Null(MaintenanceEndpoints.ValidateItem(name, null, 10_000, null));
    }

    // ── ValidateItem: intervals ──────────────────────────────────────────────
    [Fact]
    public void ValidateItem_OnlyKmIntervalSet_ReturnsNull()
    {
        Assert.Null(MaintenanceEndpoints.ValidateItem("Tyre rotation", null, 10_000, null));
    }

    [Fact]
    public void ValidateItem_OnlyMonthsIntervalSet_ReturnsNull()
    {
        Assert.Null(MaintenanceEndpoints.ValidateItem("Annual inspection", null, null, 12));
    }

    [Fact]
    public void ValidateItem_BothIntervalsSet_ReturnsNull()
    {
        Assert.Null(MaintenanceEndpoints.ValidateItem("Oil change", null, 10_000, 12));
    }

    [Fact]
    public void ValidateItem_NeitherIntervalSet_ReturnsError()
    {
        Assert.NotNull(MaintenanceEndpoints.ValidateItem("Oil change", null, null, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void ValidateItem_KmIntervalOutOfRange_ReturnsError(double intervalKm)
    {
        Assert.NotNull(MaintenanceEndpoints.ValidateItem("Oil change", null, intervalKm, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void ValidateItem_MonthsIntervalOutOfRange_ReturnsError(int intervalMonths)
    {
        Assert.NotNull(MaintenanceEndpoints.ValidateItem("Oil change", null, null, intervalMonths));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public void ValidateItem_KmIntervalAtBounds_ReturnsNull(double intervalKm)
    {
        Assert.Null(MaintenanceEndpoints.ValidateItem("Oil change", null, intervalKm, null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    public void ValidateItem_MonthsIntervalAtBounds_ReturnsNull(int intervalMonths)
    {
        Assert.Null(MaintenanceEndpoints.ValidateItem("Oil change", null, null, intervalMonths));
    }

    // ── ValidateItem: notes ──────────────────────────────────────────────────
    [Fact]
    public void ValidateItem_NotesOver1000Chars_ReturnsError()
    {
        var notes = new string('a', 1001);
        Assert.NotNull(MaintenanceEndpoints.ValidateItem("Oil change", notes, 10_000, null));
    }

    [Fact]
    public void ValidateItem_NotesExactly1000Chars_ReturnsNull()
    {
        var notes = new string('a', 1000);
        Assert.Null(MaintenanceEndpoints.ValidateItem("Oil change", notes, 10_000, null));
    }

    // ── ValidateLogEntry ──────────────────────────────────────────────────────
    [Fact]
    public void ValidateLogEntry_TodayNoOdometer_ReturnsNull()
    {
        Assert.Null(MaintenanceEndpoints.ValidateLogEntry(DateTime.UtcNow, null));
    }

    [Fact]
    public void ValidateLogEntry_ValidOdometer_ReturnsNull()
    {
        Assert.Null(MaintenanceEndpoints.ValidateLogEntry(DateTime.UtcNow, 12_345));
    }

    [Fact]
    public void ValidateLogEntry_FarInFuture_ReturnsError()
    {
        Assert.NotNull(MaintenanceEndpoints.ValidateLogEntry(DateTime.UtcNow.AddDays(5), null));
    }

    [Fact]
    public void ValidateLogEntry_NegativeOdometer_ReturnsError()
    {
        Assert.NotNull(MaintenanceEndpoints.ValidateLogEntry(DateTime.UtcNow, -1));
    }

    [Fact]
    public void ValidateLogEntry_ZeroOdometer_ReturnsNull()
    {
        Assert.Null(MaintenanceEndpoints.ValidateLogEntry(DateTime.UtcNow, 0));
    }
}
