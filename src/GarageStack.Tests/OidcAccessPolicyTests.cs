using System.Security.Claims;
using GarageStack.Api.Authentication;

namespace GarageStack.Tests;

public class OidcAccessPolicyTests
{
    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "oidc"));

    // ── No restrictions ───────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_AllowsAnyone_WhenNoAllowListIsConfigured()
    {
        var decision = OidcAccessPolicy.Evaluate(User(("email", "stranger@example.com")), new OidcOptions());

        Assert.True(decision.Allowed);
    }

    // ── Groups ────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_AllowsUserInAnAllowedGroup()
    {
        var options = new OidcOptions { AllowedGroups = "garagestack-users,admins" };
        var user = User(("groups", "photos"), ("groups", "admins"));

        var decision = OidcAccessPolicy.Evaluate(user, options);

        Assert.True(decision.Allowed);
        Assert.Contains("admins", decision.Reason);
    }

    [Fact]
    public void Evaluate_MatchesGroupsCaseInsensitively()
    {
        var options = new OidcOptions { AllowedGroups = "GarageStack-Users" };

        var decision = OidcAccessPolicy.Evaluate(User(("groups", "garagestack-users")), options);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_DeniesUserOutsideTheAllowedGroups()
    {
        var options = new OidcOptions { AllowedGroups = "garagestack-users" };

        var decision = OidcAccessPolicy.Evaluate(User(("groups", "photos")), options);

        Assert.False(decision.Allowed);
        Assert.Contains("OIDC_ALLOWED_GROUPS", decision.Reason);
    }

    [Fact]
    public void Evaluate_DeniesUserWithNoGroupsClaimAtAll()
    {
        var options = new OidcOptions { AllowedGroups = "garagestack-users" };

        var decision = OidcAccessPolicy.Evaluate(User(("email", "nobody@example.com")), options);

        Assert.False(decision.Allowed);
        Assert.Contains("no 'groups' claim", decision.Reason);
    }

    [Fact]
    public void Evaluate_ReadsGroupsFromTheConfiguredClaim()
    {
        var options = new OidcOptions { AllowedGroups = "garagestack", GroupsClaim = "roles" };

        var decision = OidcAccessPolicy.Evaluate(User(("roles", "garagestack")), options);

        Assert.True(decision.Allowed);
    }

    // ── Emails ────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_AllowsAnAllowListedEmail()
    {
        var options = new OidcOptions { AllowedEmails = "owner@example.com" };

        var decision = OidcAccessPolicy.Evaluate(User(("email", "owner@example.com")), options);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_DeniesAnEmailOutsideTheAllowList()
    {
        var options = new OidcOptions { AllowedEmails = "owner@example.com" };

        var decision = OidcAccessPolicy.Evaluate(User(("email", "someone@example.com")), options);

        Assert.False(decision.Allowed);
        Assert.Contains("OIDC_ALLOWED_EMAILS", decision.Reason);
    }

    [Fact]
    public void Evaluate_DeniesAnAllowListedEmailTheProviderReportsAsUnverified()
    {
        var options = new OidcOptions { AllowedEmails = "owner@example.com" };
        var user = User(("email", "owner@example.com"), ("email_verified", "false"));

        var decision = OidcAccessPolicy.Evaluate(user, options);

        Assert.False(decision.Allowed);
        Assert.Contains("unverified", decision.Reason);
    }

    [Fact]
    public void Evaluate_AllowsAnAllowListedEmail_WhenTheProviderOmitsEmailVerified()
    {
        // Self-hosted providers commonly leave the claim out entirely; refusing those would
        // block the most common setups.
        var options = new OidcOptions { AllowedEmails = "owner@example.com" };

        var decision = OidcAccessPolicy.Evaluate(User(("email", "owner@example.com")), options);

        Assert.True(decision.Allowed);
    }

    // ── Combined ──────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_AllowsOnEitherList_WhenBothAreConfigured()
    {
        var options = new OidcOptions
        {
            AllowedGroups = "garagestack-users",
            AllowedEmails = "owner@example.com",
        };

        Assert.True(OidcAccessPolicy.Evaluate(User(("groups", "garagestack-users")), options).Allowed);
        Assert.True(OidcAccessPolicy.Evaluate(User(("email", "owner@example.com")), options).Allowed);
        Assert.False(OidcAccessPolicy.Evaluate(User(("email", "other@example.com")), options).Allowed);
    }
}
