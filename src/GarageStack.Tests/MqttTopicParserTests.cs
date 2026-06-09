using GarageStack.Worker.Mqtt;

namespace GarageStack.Tests;

public class MqttTopicParserTests
{
    [Theory]
    [InlineData("saic/user@example.com/vehicles/FAKEVN00000000001/drivetrain/fuelLevel", "FAKEVN00000000001")]
    [InlineData("saic/user/vehicles/FAKEVN00000000002/location/latitude", "FAKEVN00000000002")]
    public void TryExtractVin_ValidTopic_ReturnsVin(string topic, string expectedVin)
    {
        var result = MqttTopicParser.TryExtractVin(topic, out var vin);
        Assert.True(result);
        Assert.Equal(expectedVin, vin);
    }

    [Theory]
    [InlineData("saic/user/notVehicles/VIN/something")]
    [InlineData("homeassistant/sensor/something")]
    [InlineData("saic/user/vehicles/VIN123/location/latitude")]  // too short / invalid VIN
    [InlineData("saic/user/vehicles/FAKEVN00000000001X/location/latitude")]  // too long (18 chars)
    [InlineData("")]
    public void TryExtractVin_InvalidTopic_ReturnsFalse(string topic)
    {
        var result = MqttTopicParser.TryExtractVin(topic, out _);
        Assert.False(result);
    }

    [Fact]
    public void ExtractSubtopic_ReturnsEverythingAfterVin()
    {
        var subtopic = MqttTopicParser.ExtractSubtopic("saic/user/vehicles/VIN/drivetrain/fuelLevel");
        Assert.Equal("drivetrain/fuelLevel", subtopic);
    }
}
