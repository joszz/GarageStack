using GarageStack.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace GarageStack.Tests;

/// <summary>
/// The typed settings the upstream clients and push notifications read. Deployments pass them as
/// environment variables, so a blank value has to mean "the default" rather than fail or switch
/// something off by accident.
/// </summary>
public class DeploymentOptionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    [Fact]
    public void Upstreams_WithBlankSettings_StayOnAndUseThePublicInstances()
    {
        var configuration = Config(
            ("Geocoding:Enabled", ""), ("Geocoding:BaseUrl", " "),
            ("MapMatching:Enabled", ""), ("MapMatching:BaseUrl", ""),
            ("Overpass:BaseUrl", ""), ("SpeedCameras:Enabled", ""));

        var geocoding = GeocodingOptions.From(configuration);
        var mapMatching = MapMatchingOptions.From(configuration);
        var overpass = OverpassOptions.From(configuration);

        Assert.True(geocoding.Enabled);
        Assert.Equal(GeocodingOptions.PublicBaseUrl, geocoding.BaseUrl);
        Assert.True(mapMatching.Enabled);
        Assert.Equal(MapMatchingOptions.PublicBaseUrl, mapMatching.BaseUrl);
        Assert.Equal(OverpassOptions.PublicBaseUrl, overpass.BaseUrl);
        Assert.True(overpass.SpeedCamerasEnabled);
    }

    [Fact]
    public void Upstreams_TakeASelfHostedInstance()
    {
        var configuration = Config(
            ("Geocoding:BaseUrl", "http://nominatim.lan"),
            ("MapMatching:BaseUrl", "http://valhalla.lan"),
            ("Overpass:BaseUrl", "http://overpass.lan/api/interpreter"));

        Assert.Equal("http://nominatim.lan", GeocodingOptions.From(configuration).BaseUrl);
        Assert.Equal("http://valhalla.lan", MapMatchingOptions.From(configuration).BaseUrl);
        Assert.Equal("http://overpass.lan/api/interpreter", OverpassOptions.From(configuration).BaseUrl);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("true", true)]
    [InlineData("off", true)]
    public void Switches_GoOffOnlyForAnExplicitFalse(string configured, bool expected)
    {
        var configuration = Config(
            ("Geocoding:Enabled", configured), ("MapMatching:Enabled", configured), ("SpeedCameras:Enabled", configured));

        Assert.Equal(expected, GeocodingOptions.From(configuration).Enabled);
        Assert.Equal(expected, MapMatchingOptions.From(configuration).Enabled);
        Assert.Equal(expected, OverpassOptions.From(configuration).SpeedCamerasEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OpenChargeMap_WithoutAKey_IsNotConfigured(string? key)
    {
        Assert.False(OpenChargeMapOptions.From(Config(("OpenChargeMap:ApiKey", key))).IsConfigured);
    }

    [Fact]
    public void Vapid_NeedsBothKeys()
    {
        var publicOnly = VapidOptions.From(Config(("Vapid:PublicKey", "public"), ("Vapid:PrivateKey", "")));
        var both = VapidOptions.From(Config(("Vapid:PublicKey", "public"), ("Vapid:PrivateKey", "private")));

        Assert.False(publicOnly.IsConfigured);
        Assert.True(both.IsConfigured);
    }

    [Theory]
    [InlineData(null, VapidOptions.FallbackSubject)]
    [InlineData("", VapidOptions.FallbackSubject)]
    [InlineData("mailto:owner@example.com", "mailto:owner@example.com")]
    public void Vapid_NamesAContactEvenWhenTheDeploymentDoesNot(string? subject, string expected)
    {
        Assert.Equal(expected, VapidOptions.From(Config(("Vapid:Subject", subject))).Subject);
    }

    // The subject reaches the browser vendors with every push, so the default is the app's own
    // https address, never anything personal.
    [Theory]
    [InlineData("https://garage.example.com", "https://garage.example.com")]
    [InlineData("https://garage.example.com:8443/", "https://garage.example.com:8443")]
    [InlineData("http://192.168.1.100:8080", VapidOptions.FallbackSubject)]
    [InlineData(null, VapidOptions.FallbackSubject)]
    public void Vapid_DefaultsToTheHttpsAddressTheAppIsServedOn(string? origin, string expected)
    {
        Assert.Equal(expected, VapidOptions.From(Config(("Cors:Origins:0", origin))).Subject);
    }

    [Theory]
    [InlineData(null, 365)]
    [InlineData("", 365)]
    [InlineData("400", 400)]
    [InlineData("30", 90)]
    [InlineData("0", null)]
    [InlineData("-1", null)]
    public void TelemetryRetention_KeepsAYear_GoesOffAtZero_AndNeverDropsBelowNinetyDays(string? configured, int? expected)
    {
        Assert.Equal(expected, TelemetryRetentionOptions.From(Config(("TelemetryRetention:FullDetailDays", configured))).FullDetailDays);
    }

    // ── RDW ───────────────────────────────────────────────────────────────────

    // The plate leaves the network only when the operator asks for it.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("false")]
    public void Rdw_IsOffUnlessExplicitlyOn(string? configured)
    {
        Assert.False(RdwOptions.From(Config(("Rdw:Enabled", configured))).Enabled);
    }

    [Fact]
    public void Rdw_WithBlankSettings_RefreshesDailyWithoutAToken()
    {
        var rdw = RdwOptions.From(Config(("Rdw:Enabled", "true"), ("Rdw:RefreshHours", ""), ("Rdw:AppToken", " ")));

        Assert.True(rdw.Enabled);
        Assert.Equal(TimeSpan.FromHours(24), rdw.RefreshInterval);
        Assert.Null(rdw.AppToken);
    }

    [Theory]
    [InlineData("6", 6)]
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("often", 24)]
    public void Rdw_RefreshHours_IsAnHourAtTheLeast(string configured, int expected)
    {
        Assert.Equal(expected, RdwOptions.From(Config(("Rdw:RefreshHours", configured))).RefreshHours);
    }

    [Fact]
    public void Rdw_TakesTheVariables()
    {
        var configuration = new ConfigurationBuilder()
            .AddGarageStackEnvironment(new Dictionary<string, string?>
            {
                ["RDW_ENABLED"] = "true",
                ["RDW_REFRESH_HOURS"] = "12",
                ["RDW_APP_TOKEN"] = "app-token",
            })
            .Build();

        var rdw = RdwOptions.From(configuration);

        Assert.True(rdw.Enabled);
        Assert.Equal(12, rdw.RefreshHours);
        Assert.Equal("app-token", rdw.AppToken);
    }
}
