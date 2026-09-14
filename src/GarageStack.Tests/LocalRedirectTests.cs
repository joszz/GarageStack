using GarageStack.Api.Authentication;

namespace GarageStack.Tests;

public class LocalRedirectTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/map", "/map")]
    [InlineData("/statistics?days=30", "/statistics?days=30")]
    [InlineData("/maintenance#history", "/maintenance#history")]
    public void Sanitize_KeepsLocalPaths(string returnUrl, string expected)
    {
        Assert.Equal(expected, LocalRedirect.Sanitize(returnUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.example.com")]
    [InlineData("//evil.example.com")]
    [InlineData("/\\evil.example.com")]
    [InlineData("map")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/map\r\nLocation: https://evil.example.com")]
    public void Sanitize_FallsBackToTheStartPage_ForAnythingNotLocal(string? returnUrl)
    {
        Assert.Equal(LocalRedirect.Default, LocalRedirect.Sanitize(returnUrl));
    }

    [Fact]
    public void Sanitize_RejectsOverlyLongValues()
    {
        var returnUrl = "/" + new string('a', 1000);

        Assert.Equal(LocalRedirect.Default, LocalRedirect.Sanitize(returnUrl));
    }
}
