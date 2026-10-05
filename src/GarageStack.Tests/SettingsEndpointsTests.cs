using System.Security.Claims;
using System.Text.Json;
using GarageStack.Api.Authentication;
using GarageStack.Api.Endpoints;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class SettingsEndpointsTests
{
    private static string? CodeFor(string json)
    {
        using var document = JsonDocument.Parse(json);
        return SettingsEndpoints.Validate(document.RootElement)?.Code;
    }

    [Fact]
    public void Validate_AcceptsTheBrowsersOwnKeys()
    {
        Assert.Null(CodeFor("""{"theme":"light","units":{"distance":"mi"},"statsInsights":[],"chargingMinPowerKw":50}"""));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"light\"")]
    [InlineData("null")]
    public void Validate_RefusesAnythingButAnObject(string json) =>
        Assert.Equal("settings.notAnObject", CodeFor(json));

    [Fact]
    public void Validate_RefusesASaveOfNothing() =>
        Assert.Equal("settings.empty", CodeFor("{}"));

    [Theory]
    [InlineData("__proto__")]
    [InlineData("constructor-name")]
    [InlineData("1theme")]
    [InlineData("the me")]
    [InlineData("")]
    public void Validate_RefusesKeysThatAreNotPlainIdentifiers(string key) =>
        Assert.Equal("settings.invalidKey", CodeFor($$"""{"{{key}}":true}"""));

    [Fact]
    public void Validate_RefusesAKeyLongerThanTheLimit() =>
        Assert.Equal("settings.invalidKey", CodeFor($$"""{"{{new string('a', UserSettingsLimits.KeyMaxLength + 1)}}":true}"""));

    [Fact]
    public void Validate_RefusesMoreKeysThanASectionMayHold()
    {
        var keys = Enumerable.Range(0, UserSettingsLimits.MaxKeys + 1).Select(i => $"\"key{i}\":{i}");

        Assert.Equal("settings.tooManyKeys", CodeFor($"{{{string.Join(',', keys)}}}"));
    }

    // ── The account key ───────────────────────────────────────────────────────

    private static ClaimsPrincipal Session(string? subject, string method) =>
        SessionPrincipal.Create("someone", subject, method, SessionPrincipal.NewSessionId());

    [Fact]
    public void AccountKey_IsTheSameForEverySessionOfOneAccount()
    {
        var first = SessionPrincipal.ResolveAccountKey(Session("alice", "OpenIdConnect"));
        var second = SessionPrincipal.ResolveAccountKey(Session("alice", "OpenIdConnect"));

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void AccountKey_KeepsAPasswordAccountApartFromAProviderAccountOfTheSameName()
    {
        Assert.NotEqual(
            SessionPrincipal.ResolveAccountKey(Session("demo", "Cookies")),
            SessionPrincipal.ResolveAccountKey(Session("demo", "OpenIdConnect")));
    }

    [Fact]
    public void AccountKey_HoldsNoTraceOfTheSubject()
    {
        var key = SessionPrincipal.ResolveAccountKey(Session("driver@example.com", "Cookies"));

        Assert.DoesNotContain("driver", key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountKey_IsNull_ForASessionWithoutASubject() =>
        Assert.Null(SessionPrincipal.ResolveAccountKey(Session(null, "OpenIdConnect")));
}
