using GarageStack.Worker.Mqtt;

namespace GarageStack.Tests;

public class MqttTopicParserTests
{
    [Theory]
    [InlineData("saic/user@example.com/vehicles/LSJW94398TG031833/drivetrain/fuelLevel", "LSJW94398TG031833")]
    [InlineData("saic/user/vehicles/VIN123/location/latitude", "VIN123")]
    public void TryExtractVin_ValidTopic_ReturnsVin(string topic, string expectedVin)
    {
        var result = MqttTopicParser.TryExtractVin(topic, out var vin);
        Assert.True(result);
        Assert.Equal(expectedVin, vin);
    }

    [Theory]
    [InlineData("saic/user/notVehicles/VIN/something")]
    [InlineData("homeassistant/sensor/something")]
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
