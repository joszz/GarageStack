using GarageStack.Worker.Mqtt;

namespace GarageStack.Tests;

public class GatewayVehicleMessageTests
{
    [Fact]
    public void TryParse_GatewayEvent_ReadsTitleContentAndType()
    {
        const string payload = """
            {"event_type":"vehicle_message","title":" Vehicle alarm ","content":"The alarm went off.","message_type":"301","sender":"iSMART","vin":"FAKEVN00000000001"}
            """;

        Assert.True(GatewayVehicleMessage.TryParse(payload, out var message));

        Assert.Equal("Vehicle alarm", message.Title);
        Assert.Equal("The alarm went off.", message.Content);
        Assert.Equal("301", message.MessageType);
    }

    // The gateway writes "" for a field SAIC left out; a non-string value is not text to show.
    [Fact]
    public void TryParse_MissingOrNonStringFields_ReadAsEmpty()
    {
        Assert.True(GatewayVehicleMessage.TryParse("""{"title":null,"content":42}""", out var message));

        Assert.Equal(string.Empty, message.Title);
        Assert.Equal(string.Empty, message.Content);
        Assert.Equal(string.Empty, message.MessageType);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void TryParse_NotAJsonObject_ReturnsFalse(string payload)
    {
        Assert.False(GatewayVehicleMessage.TryParse(payload, out _));
    }

    [Fact]
    public void TryParse_LongText_IsClipped()
    {
        var title = new string('t', GatewayVehicleMessage.MaxTitleLength + 50);
        var content = new string('c', GatewayVehicleMessage.MaxContentLength + 50);

        Assert.True(GatewayVehicleMessage.TryParse($$"""{"title":"{{title}}","content":"{{content}}"}""", out var message));

        Assert.Equal(GatewayVehicleMessage.MaxTitleLength, message.Title.Length);
        Assert.Equal(GatewayVehicleMessage.MaxContentLength, message.Content.Length);
    }

    [Theory]
    [InlineData("1234567890123456789", "1234567890123456789")]
    [InlineData(" 42 ", "42")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void ParseId_ReadsTrimmedId(string payload, string? expected)
    {
        Assert.Equal(expected, GatewayVehicleMessage.ParseId(payload));
    }

    [Fact]
    public void ParseId_TooLongToBeAnId_ReturnsNull()
    {
        Assert.Null(GatewayVehicleMessage.ParseId(new string('9', 65)));
    }

    [Fact]
    public void ParseSentAt_GatewayIsoString_ReadsInstant()
    {
        var sentAt = GatewayVehicleMessage.ParseSentAt("2026-03-14T10:00:00+00:00");

        Assert.Equal(new DateTimeOffset(2026, 3, 14, 10, 0, 0, TimeSpan.Zero), sentAt);
    }

    // The gateway always adds an offset, but a time without one is taken for UTC, as the gateway does.
    [Fact]
    public void ParseSentAt_NoOffset_AssumesUtc()
    {
        var sentAt = GatewayVehicleMessage.ParseSentAt("2026-03-14 10:00:00");

        Assert.Equal(new DateTimeOffset(2026, 3, 14, 10, 0, 0, TimeSpan.Zero), sentAt);
    }

    [Fact]
    public void ParseSentAt_Unreadable_ReturnsNull()
    {
        Assert.Null(GatewayVehicleMessage.ParseSentAt("yesterday"));
    }
}
