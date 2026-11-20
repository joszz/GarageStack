using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class TimeOfDayTextTests
{
    [Theory]
    [InlineData("00:00")]
    [InlineData("07:30")]
    [InlineData("23:59")]
    public void TryParse_TakesA24HourTime(string text)
    {
        Assert.True(TimeOfDayText.TryParse(text, out var time));
        Assert.Equal(text, TimeOfDayText.ToText(time));
    }

    // .NET 11 reads "24:00" as midnight on its own; the gateway only knows 00 to 23.
    [Theory]
    [InlineData("24:00")]
    [InlineData("7:30")]
    [InlineData("07:60")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RefusesAnythingElse(string? text)
    {
        Assert.False(TimeOfDayText.TryParse(text, out _));
    }
}
