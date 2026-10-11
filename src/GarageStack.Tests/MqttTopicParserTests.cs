using GarageStack.Worker.Mqtt;

namespace GarageStack.Tests;

public class MqttTopicParserTests
{
    [Fact]
    public void TryParse_ValidTopic_ReturnsUserVinAndSubtopicInOnePass()
    {
        var ok = MqttTopicParser.TryParse("saic/user@example.com/vehicles/FAKEVN00000000001/drivetrain/fuelLevel", out var parsed);

        Assert.True(ok);
        Assert.Equal("user@example.com", parsed.User);
        Assert.Equal("FAKEVN00000000001", parsed.Vin);
        Assert.Equal("drivetrain/fuelLevel", parsed.Subtopic);
    }

    [Fact]
    public void TryParse_TopicEndingAtTheVin_HasAnEmptySubtopic()
    {
        Assert.True(MqttTopicParser.TryParse("saic/user/vehicles/FAKEVN00000000001", out var parsed));

        Assert.Equal("FAKEVN00000000001", parsed.Vin);
        Assert.Equal(string.Empty, parsed.Subtopic);
    }

    [Theory]
    [InlineData("saic/user/notVehicles/FAKEVN00000000001/something")]
    [InlineData("homeassistant/sensor/something")]
    [InlineData("saic/user/vehicles/VIN123/location/latitude")]  // too short
    [InlineData("saic/user/vehicles/FAKEVN00000000001X/location/latitude")]  // too long (18 chars)
    [InlineData("")]
    public void TryParse_NotAVehicleTopicWithAWellFormedVin_ReturnsFalse(string topic)
    {
        Assert.False(MqttTopicParser.TryParse(topic, out _));
    }

    [Fact]
    public void TryParseAccount_AccountTopic_ReturnsUserAndSubtopic()
    {
        Assert.True(MqttTopicParser.TryParseAccount("saic/user@example.com/account/lastLoginError", out var parsed));

        Assert.Equal("user@example.com", parsed.User);
        Assert.Equal("lastLoginError", parsed.Subtopic);
    }

    [Theory]
    [InlineData("saic/user/vehicles/FAKEVN00000000001/drivetrain/soc")]
    [InlineData("saic/user/account")]
    [InlineData("homeassistant/sensor/account/state")]
    public void TryParseAccount_NotAnAccountTopic_ReturnsFalse(string topic)
    {
        Assert.False(MqttTopicParser.TryParseAccount(topic, out _));
    }
}
