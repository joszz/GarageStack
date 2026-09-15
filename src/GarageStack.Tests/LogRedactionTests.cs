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
}
