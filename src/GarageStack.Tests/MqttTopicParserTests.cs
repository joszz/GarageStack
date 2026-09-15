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
    public void TryParse_ValidTopic_ReturnsUserVinAndSubtopicInOnePass()
    {
        var ok = MqttTopicParser.TryParse("saic/user@example.com/vehicles/FAKEVN00000000001/drivetrain/fuelLevel", out var parsed);

        Assert.True(ok);
        Assert.Equal("user@example.com", parsed.User);
        Assert.Equal("FAKEVN00000000001", parsed.Vin);
        Assert.Equal("drivetrain/fuelLevel", parsed.Subtopic);
    }

    [Theory]
    [InlineData("saic/user/vehicles/FAKEVN00000000001")]
    [InlineData("saic/user/vehicles/VIN123/drivetrain/fuelLevel")]
    [InlineData("homeassistant/sensor/something")]
    public void TryParse_TopicWithoutSubtopicOrInvalidVin_HandlesBothCases(string topic)
    {
        var ok = MqttTopicParser.TryParse(topic, out var parsed);

        // The first case is a well-formed vehicle topic with nothing after the VIN.
        if (topic.EndsWith("FAKEVN00000000001", StringComparison.Ordinal))
        {
            Assert.True(ok);
            Assert.Equal(string.Empty, parsed.Subtopic);
        }
        else
        {
            Assert.False(ok);
        }
    }

    [Fact]
    public void ExtractSubtopic_ReturnsEverythingAfterVin()
    {
        var subtopic = MqttTopicParser.ExtractSubtopic("saic/user/vehicles/VIN/drivetrain/fuelLevel");
        Assert.Equal("drivetrain/fuelLevel", subtopic);
    }
}
