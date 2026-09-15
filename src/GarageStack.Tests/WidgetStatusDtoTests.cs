using GarageStack.Api.Endpoints;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;

namespace GarageStack.Tests;

public class WidgetStatusDtoTests
{
    [Fact]
    public void FromSnapshot_PowerUsageOfDay_PassesKwhThrough()
    {
        // The gateway already publishes kWh; dividing by 1000 turned 16.34 kWh into 0.02 (issue #284)
        var snapshot = new TelemetrySnapshot { PowerUsageOfDay = 16.337 };

        var dto = WidgetStatusDto.FromSnapshot(snapshot, new KeyEchoLocalizer());

        Assert.Equal(16.34, dto.PowerUsageOfDayKwh);
    }

    [Fact]
    public void FromSnapshot_NoPowerUsageOfDay_ReturnsNull()
    {
        var dto = WidgetStatusDto.FromSnapshot(new TelemetrySnapshot(), new KeyEchoLocalizer());

        Assert.Null(dto.PowerUsageOfDayKwh);
    }

    private sealed class KeyEchoLocalizer : IStringLocalizer<WidgetStrings>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
