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
}
