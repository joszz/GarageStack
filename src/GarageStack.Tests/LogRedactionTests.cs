using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class LogRedactionTests
{
    [Fact]
    public void Vin_KeepsOnlyTheLastFourCharacters()
    {
        Assert.Equal("***0001", LogRedaction.Vin("FAKEVN00000000001"));
    }

    [Theory]
    [InlineData(null, "***")]
    [InlineData("", "***")]
    [InlineData("AB", "**")]
    public void Vin_ShortOrMissingValues_NeverLeakAnything(string? vin, string expected)
    {
        Assert.Equal(expected, LogRedaction.Vin(vin));
    }

    [Fact]
    public void MqttTopic_RedactsAccountEmailAndVin_KeepsCommandPath()
    {
        var redacted = LogRedaction.MqttTopic("saic/user@example.com/vehicles/FAKEVN00000000001/doors/locked/set");

        Assert.Equal("saic/***/vehicles/***0001/doors/locked/set", redacted);
        Assert.DoesNotContain("user@example.com", redacted);
    }

    [Fact]
    public void MqttTopic_NonVehicleTopic_IsLeftAloneApartFromLineEndings()
    {
        Assert.Equal("homeassistant/sensor/x config", LogRedaction.MqttTopic("homeassistant/sensor/x\nconfig"));
    }

    // Every /api/vehicles/{vin}/... request is logged at Information level.
    [Theory]
    [InlineData("/api/vehicles/FAKEVN00000000001/status", "/api/vehicles/***0001/status")]
    [InlineData("/api/vehicles/FAKEVN00000000001", "/api/vehicles/***0001")]
    [InlineData("/api/widget/FAKEVN00000000001/status", "/api/widget/***0001/status")]
    [InlineData("/api/vehicles", "/api/vehicles")]
    [InlineData("/api/trips/FAKEVN0000000001", "/api/trips/FAKEVN0000000001")]
    public void RequestPath_ShortensEveryVinSegment(string path, string expected)
    {
        Assert.Equal(expected, LogRedaction.RequestPath(path));
    }
}

public class AccountAndUrlRedactionTests
{
    [Theory]
    [InlineData("jos", "jos")]
    [InlineData("Household Admin", "Household Admin")]
    [InlineData("someone@example.com", "s***@example.com")]
    [InlineData("@example.com", "@example.com")]
    [InlineData(null, "***")]
    [InlineData(" ", "***")]
    public void Account_ShortensOnlyAnEmailAddress(string? name, string expected)
    {
        Assert.Equal(expected, LogRedaction.Account(name));
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc123:APA91bSecret", "fcm.googleapis.com")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/gAAAAsecret", "updates.push.services.mozilla.com")]
    [InlineData("not a url", "***")]
    [InlineData(null, "***")]
    public void UrlHost_KeepsOnlyThePushService(string? url, string expected)
    {
        Assert.Equal(expected, LogRedaction.UrlHost(url));
    }
}

public class VinFormatTests
{
    [Theory]
    [InlineData("FAKEVN00000000001", true)]
    [InlineData("FAKEVN0000000001", false)]
    [InlineData("FAKEVN000000000012", false)]
    [InlineData("FAKEVNO0000000001", false)]
    [InlineData("zigbee2mqtt_0x00158d0001", false)]
    [InlineData(null, false)]
    public void IsValid_TakesOnlyAWellFormedVin(string? value, bool valid)
    {
        Assert.Equal(valid, VinFormat.IsValid(value));
    }
}
