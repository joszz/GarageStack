using System.Globalization;
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
        // Dutch writes a decimal comma, so a culture-sensitive parse would read "3.15" as 315.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
        try
        {
            var thresholds = Resolve(Config(("TyrePressure:HighBar", "3.15")));

            Assert.Equal(3.15, thresholds.HighBar);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static HvBatteryCapacity ResolveCapacity(IConfiguration configuration) =>
        new ServiceCollection()
            .AddHvBatteryCapacity(configuration)
            .BuildServiceProvider()
            .GetRequiredService<HvBatteryCapacity>();

    [Fact]
    public void HvBatteryCapacity_WhenConfigured_OverridesWhatTheGatewayAssumes()
    {
        var capacity = ResolveCapacity(Config(("HvBattery:CapacityKwh", "1.83")));

        Assert.Equal(1.83, capacity.Kwh);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a number")]
    [InlineData("0")]
    [InlineData("-5")]
    public void HvBatteryCapacity_WithAnUnusableValue_StaysUnknown(string? configured)
    {
        var capacity = ResolveCapacity(Config(("HvBattery:CapacityKwh", configured)));

        Assert.Equal(HvBatteryCapacity.Unknown, capacity);
        Assert.Null(capacity.Kwh);
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

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("nonsense", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("true", true)]
    public void SwitchOrDefault_OnlyAnExplicitValueOverridesTheFallback(string? configured, bool expected)
    {
        var configuration = Config(("Geocoding:Enabled", configured));

        Assert.Equal(expected, configuration.SwitchOrDefault("Geocoding:Enabled", fallback: true));
    }

    [Theory]
    [InlineData(null, "fallback")]
    [InlineData("", "fallback")]
    [InlineData("   ", "fallback")]
    [InlineData("http://nominatim.lan", "http://nominatim.lan")]
    public void TextOrDefault_FallsBackForBlankValues(string? configured, string expected)
    {
        var configuration = Config(("Geocoding:BaseUrl", configured));

        Assert.Equal(expected, configuration.TextOrDefault("Geocoding:BaseUrl", "fallback"));
    }
}
