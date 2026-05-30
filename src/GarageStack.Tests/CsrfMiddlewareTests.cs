using GarageStack.Api;

namespace GarageStack.Tests;

public class CsrfMiddlewareTests
{
    private static readonly string[] Allowed = ["https://app.garagestack.io", "https://www.garagestack.io"];

    [Theory]
    [InlineData("https://app.garagestack.io")]
    [InlineData("https://www.garagestack.io")]
    [InlineData("HTTPS://APP.GARAGESTACK.IO")]
    [InlineData("Https://App.Garagestack.Io")]
    public void IsOriginAllowed_KnownOrigin_ReturnsTrue(string origin)
    {
        Assert.True(CsrfPolicy.IsOriginAllowed(origin, Allowed));
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("https://garagestack.io.evil.com")]
    [InlineData("http://app.garagestack.io")]
    [InlineData("https://app.garagestack.io.evil.com")]
    public void IsOriginAllowed_UnknownOrigin_ReturnsFalse(string origin)
    {
        Assert.False(CsrfPolicy.IsOriginAllowed(origin, Allowed));
    }

    [Fact]
    public void IsOriginAllowed_EmptyAllowedList_ReturnsFalse()
    {
        Assert.False(CsrfPolicy.IsOriginAllowed("https://app.garagestack.io", []));
    }
}
