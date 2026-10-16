using GarageStack.Api.Authentication;
using Microsoft.AspNetCore.Http;

namespace GarageStack.Tests;

public class CookieSecurePolicyTests
{
    [Theory]
    [InlineData("auto", false, CookieSecurePolicy.SameAsRequest)]
    [InlineData(" AUTO ", false, CookieSecurePolicy.SameAsRequest)]
    [InlineData("true", false, CookieSecurePolicy.Always)]
    [InlineData("True", true, CookieSecurePolicy.Always)]
    [InlineData("false", false, CookieSecurePolicy.None)]
    [InlineData(null, false, CookieSecurePolicy.Always)]
    [InlineData("", false, CookieSecurePolicy.Always)]
    [InlineData(null, true, CookieSecurePolicy.SameAsRequest)]
    public void TheSettingPicksThePolicy(string? setting, bool isDevelopment, CookieSecurePolicy expected)
    {
        Assert.Equal(expected, AuthenticationSetup.ResolveCookieSecurePolicy(setting, isDevelopment));
    }
}
