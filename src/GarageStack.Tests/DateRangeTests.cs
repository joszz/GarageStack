using GarageStack.Api.Endpoints;

namespace GarageStack.Tests;

public class DateRangeTests
{
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly TimeSpan Year = TimeSpan.FromDays(366);

    [Fact]
    public void AMissingStart_IsTheDefaultSpanBeforeTheEnd()
    {
        var to = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(DateRange.TryResolve(null, to, Week, Year, out var start, out var end));

        Assert.Equal(to.UtcDateTime, end);
        Assert.Equal(to.UtcDateTime - Week, start);
    }

    [Fact]
    public void ARangeLongerThanAllowed_IsCutFromTheStart()
    {
        var from = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

        Assert.Null(DateRange.TryResolve(from, to, Week, Year, out var start, out var end));

        Assert.Equal(to.UtcDateTime, end);
        Assert.Equal(to.UtcDateTime - Year, start);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AStartThatIsNotBeforeTheEnd_IsRefused(int hoursAfterEnd)
    {
        var to = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

        Assert.NotNull(DateRange.TryResolve(to.AddHours(-hoursAfterEnd), to, Week, Year, out _, out _));
    }
}
