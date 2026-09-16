using GarageStack.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GarageStack.Tests;

/// <summary>
/// Deployments configure the API and the Worker through environment variables, where an unset
/// setting arrives as an empty string rather than as a missing key. These cover that: the app
/// holds the defaults, so the compose files and the all-in-one entrypoint no longer restate them.
/// </summary>
public class HostingExtensionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static TyrePressureThresholds Resolve(IConfiguration configuration) =>
        new ServiceCollection()
            .AddTyrePressureThresholds(configuration)
            .BuildServiceProvider()
            .GetRequiredService<TyrePressureThresholds>();

    [Fact]
    public void TyrePressureThresholds_WithNothingConfigured_UsesTheBuiltInDefaults()
    {
        var thresholds = Resolve(Config());

        Assert.Equal(TyrePressureThresholds.Default, thresholds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a number")]
    public void TyrePressureThresholds_WithAnUnusableValue_FallsBackToTheDefault(string configured)
    {
        var thresholds = Resolve(Config(("TyrePressure:LowBar", configured)));

        Assert.Equal(TyrePressureThresholds.Default.LowBar, thresholds.LowBar);
    }

    [Fact]
    public void TyrePressureThresholds_OverridesOnlyTheValuesThatAreSet()
    {
        var thresholds = Resolve(Config(("TyrePressure:GoodBar", "2.55")));

        Assert.Equal(2.55, thresholds.GoodBar);
        Assert.Equal(TyrePressureThresholds.Default.LowBar, thresholds.LowBar);
        Assert.Equal(TyrePressureThresholds.Default.HighBar, thresholds.HighBar);
    }

    [Fact]
    public void TyrePressureThresholds_ReadsDecimalsThesameWayInEveryLocale()
    {
        var thresholds = Resolve(Config(("TyrePressure:HighBar", "3.15")));

        Assert.Equal(3.15, thresholds.HighBar);
    }

    [Theory]
    [InlineData(null, 120)]
    [InlineData("", 120)]
    [InlineData("nonsense", 120)]
    [InlineData("500", 500)]
    public void IntegerOrDefault_FallsBackUnlessTheValueIsUsable(string? configured, int expected)
    {
        var configuration = Config(("RateLimits:GlobalPerMinute", configured));

        Assert.Equal(expected, configuration.IntegerOrDefault("RateLimits:GlobalPerMinute", 120));
    }
}
