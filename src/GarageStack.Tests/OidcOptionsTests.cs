using GarageStack.Api.Authentication;
using Microsoft.Extensions.Configuration;

namespace GarageStack.Tests;

public class OidcOptionsTests
{
    private static OidcOptions Bind(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return config.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();
    }

    // ── Enablement ────────────────────────────────────────────────────────────

    [Fact]
    public void Enabled_IsFalse_WhenNothingIsConfigured()
    {
        Assert.False(Bind().Enabled);
    }

    [Fact]
    public void Enabled_IsTrue_WhenAuthorityIsSet()
    {
        var options = Bind(
            ("Oidc:Authority", "https://auth.example.com"),
            ("Oidc:ClientId", "garagestack"));

        Assert.True(options.Enabled);
    }

    [Fact]
    public void EnvironmentVariableStyleKeys_BindToTheSameOptions()
    {
        // Docker passes Oidc__Authority; ASP.NET Core maps "__" to ":" before binding.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new("Oidc:Authority", "https://auth.example.com"),
                new("Oidc:ClientId", "garagestack"),
                new("Oidc:AutoLogin", "true"),
                new("Oidc:ProviderName", "Authentik"),
                new("Oidc:RequireHttpsMetadata", "false"),
            ])
            .Build();

        var options = config.GetSection(OidcOptions.SectionName).Get<OidcOptions>()!;

        Assert.Equal("https://auth.example.com", options.Authority);
        Assert.Equal("garagestack", options.ClientId);
        Assert.True(options.AutoLogin);
        Assert.Equal("Authentik", options.ProviderName);
        Assert.False(options.RequireHttpsMetadata);
    }

    [Fact]
    public void Defaults_CoverTheCommonProviderSetup()
    {
        var options = new OidcOptions();

        Assert.Equal("SSO", options.ProviderName);
        Assert.Equal("groups", options.GroupsClaim);
        Assert.True(options.RequireHttpsMetadata);
        Assert.False(options.AutoLogin);
        Assert.Equal(["openid", "profile", "email"], options.ScopeList);
    }

    // ── List parsing ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("admins,car-users", new[] { "admins", "car-users" })]
    [InlineData("admins, car-users", new[] { "admins", "car-users" })]
    [InlineData(" admins ; car-users ", new[] { "admins", "car-users" })]
    [InlineData("", new string[0])]
    public void AllowedGroupList_IsSplitOnCommonSeparators(string configured, string[] expected)
    {
        var options = new OidcOptions { AllowedGroups = configured };

        Assert.Equal(expected, options.AllowedGroupList);
    }

    [Fact]
    public void HasAccessRestrictions_IsFalse_WhenNoAllowListIsConfigured()
    {
        Assert.False(new OidcOptions().HasAccessRestrictions);
    }

    [Fact]
    public void HasAccessRestrictions_IsTrue_WithAnEmailAllowList()
    {
        Assert.True(new OidcOptions { AllowedEmails = "me@example.com" }.HasAccessRestrictions);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_Passes_WhenNothingIsConfigured()
    {
        var options = new OidcOptions();

        options.Validate();

        Assert.False(options.Enabled);
    }

    [Fact]
    public void Validate_Throws_WhenClientIdIsSetWithoutAuthority()
    {
        var options = new OidcOptions { ClientId = "garagestack" };

        var ex = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("OIDC_AUTHORITY", ex.Message);
    }

    [Fact]
    public void Validate_Throws_WhenAuthorityIsSetWithoutClientId()
    {
        var options = new OidcOptions { Authority = "https://auth.example.com" };

        var ex = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("OIDC_CLIENT_ID", ex.Message);
    }

    [Theory]
    [InlineData("auth.example.com")]
    [InlineData("ftp://auth.example.com")]
    [InlineData("not a url")]
    public void Validate_Throws_WhenAuthorityIsNotAnHttpUrl(string authority)
    {
        var options = new OidcOptions { Authority = authority, ClientId = "garagestack" };

        var ex = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("OIDC_AUTHORITY", ex.Message);
    }

    [Fact]
    public void Validate_Throws_WhenRedirectUriPointsAtADifferentPath()
    {
        var options = new OidcOptions
        {
            Authority = "https://auth.example.com",
            ClientId = "garagestack",
            RedirectUri = "https://garage.example.com/callback",
        };

        var ex = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(OidcOptions.CallbackPath, ex.Message);
    }

    [Fact]
    public void Validate_Accepts_ARedirectUriOnTheCallbackPath()
    {
        var options = new OidcOptions
        {
            Authority = "https://auth.example.com",
            ClientId = "garagestack",
            RedirectUri = $"https://garage.example.com{OidcOptions.CallbackPath}",
        };

        options.Validate();

        Assert.True(options.Enabled);
    }

    [Fact]
    public void Validate_AddsTheOpenIdScope_WhenItIsMissing()
    {
        var options = new OidcOptions
        {
            Authority = "https://auth.example.com",
            ClientId = "garagestack",
            Scopes = "profile email",
        };

        options.Validate();

        Assert.Equal(["openid", "profile", "email"], options.ScopeList);
    }
}
