using System.Net;
using System.Net.Http.Json;
using GarageStack.Api;
using GarageStack.Api.Authentication;
using GarageStack.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GarageStack.Tests;

/// <summary>
/// Drives the real ASP.NET Core authentication stack end to end against
/// <see cref="FakeIdentityProvider"/>: challenge, provider round-trip, code redemption over the
/// backchannel, claim mapping, the session cookie, and logout revocation.
///
/// All host-starting tests live in this one class on purpose. Demo mode shares a single in-memory
/// database across hosts, and xUnit runs classes in parallel but the tests inside a class
/// sequentially, so keeping them together avoids two hosts seeding that database at once.
/// </summary>
public class AuthenticationFlowTests
{
    private const string DemoUsername = "demo";
    private const string DemoPassword = "demo-password";

    // ── OIDC: challenge ───────────────────────────────────────────────────────

    [Fact]
    public async Task OidcLogin_RedirectsToProvider_WithAuthorizationCodeFlowAndPkce()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        await using var factory = CreateOidcFactory(idp);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/auth/oidc/login?returnUrl=/map", ct);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = response.Headers.Location!;
        Assert.Equal($"{FakeIdentityProvider.IssuerUrl}/authorize", location.GetLeftPart(UriPartial.Path));

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal(FakeIdentityProvider.ClientId, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        // The handler leaves response_mode out when it matches the default for the response type,
        // and "query" is the default for "code" -- so the parameter being absent is exactly what
        // confirms this is not form_post, whose cross-site POST back carries no Lax cookies.
        Assert.Equal("query", query.TryGetValue("response_mode", out var responseMode) ? responseMode.ToString() : "query");
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
        Assert.False(string.IsNullOrWhiteSpace(query["nonce"]));
        Assert.Equal($"http://localhost{OidcOptions.CallbackPath}", query["redirect_uri"]);
        Assert.Contains("openid", query["scope"].ToString().Split(' '));
    }

    [Fact]
    public async Task OidcLogin_UsesConfiguredRedirectUri_WhenSet()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        await using var factory = CreateOidcFactory(idp, settings =>
            settings["Oidc__RedirectUri"] = $"https://garage.example.com{OidcOptions.CallbackPath}");
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/auth/oidc/login", ct);

        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal($"https://garage.example.com{OidcOptions.CallbackPath}", query["redirect_uri"]);
    }

    [Fact]
    public async Task OidcLogin_WhenTheProviderRefusesTheClient_RedirectsToLoginInsteadOfFailing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider { RejectPushedAuthorizationRequests = true };
        await using var factory = CreateOidcFactory(idp);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/auth/oidc/login", ct);

        // A wrong client secret or client authentication method must not leave a browser
        // navigation staring at a JSON 500 -- and with auto-login on, looping through them.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/login?error=oidc_failed", response.Headers.Location!.OriginalString);
    }

    // ── OIDC: callback ────────────────────────────────────────────────────────

    [Fact]
    public async Task OidcCallback_SignsInAndRedirectsToReturnUrl()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        idp.UserInfoClaims["preferred_username"] = "alice";
        idp.UserInfoClaims["email"] = "alice@example.com";

        await using var factory = CreateOidcFactory(idp);
        using var client = CreateClient(factory);

        var callback = await SignInAsync(client, idp, "/map", ct);

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal("/map", callback.Headers.Location!.OriginalString);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", ct);
        Assert.Equal("alice", me!.Username);
        Assert.NotNull(me.ExpiresAtUtc);
    }

    [Fact]
    public async Task OidcCallback_RedeemsTheCodeWithPkceVerifierAndClientSecret()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        await using var factory = CreateOidcFactory(idp);
        using var client = CreateClient(factory);

        await SignInAsync(client, idp, "/", ct);

        Assert.NotNull(idp.LastTokenRequestBody);
        Assert.Contains("grant_type=authorization_code", idp.LastTokenRequestBody);
        Assert.Contains("code=test-auth-code", idp.LastTokenRequestBody);
        Assert.Contains("code_verifier=", idp.LastTokenRequestBody);
        Assert.Contains(FakeIdentityProvider.ClientSecret, idp.LastTokenRequestBody);
    }

    [Fact]
    public async Task OidcCallback_RejectsNonLocalReturnUrl()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        await using var factory = CreateOidcFactory(idp);
        using var client = CreateClient(factory);

        var callback = await SignInAsync(client, idp, "https://evil.example.com/steal", ct);

        // Signed in, but sent to the app's own start page instead of the attacker's site.
        Assert.Equal(LocalRedirect.Default, callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task OidcCallback_WithUnknownState_RedirectsToLoginWithError()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        await using var factory = CreateOidcFactory(idp);
        using var client = CreateClient(factory);

        // No challenge first, so there is no correlation cookie backing this state.
        var callback = await client.GetAsync($"{OidcOptions.CallbackPath}?code=abc&state=made-up", ct);

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal("/login?error=oidc_failed", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task OidcCallback_RejectsAnIdTokenSignedWithAnUnpublishedKey()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider { SignWithUntrustedKey = true };
        await using var factory = CreateOidcFactory(idp);
        using var client = CreateClient(factory);

        var callback = await SignInAsync(client, idp, "/", ct);

        Assert.Equal("/login?error=oidc_failed", callback.Headers.Location!.OriginalString);

        var me = await client.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    // ── OIDC: access restrictions ─────────────────────────────────────────────

    [Fact]
    public async Task OidcCallback_AllowsUserInAllowedGroup_FromUserInfo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        idp.UserInfoClaims["preferred_username"] = "bob";
        idp.UserInfoClaims["groups"] = new[] { "media", "garagestack-users" };

        await using var factory = CreateOidcFactory(idp, settings =>
            settings["Oidc__AllowedGroups"] = "garagestack-users");
        using var client = CreateClient(factory);

        var callback = await SignInAsync(client, idp, "/", ct);

        Assert.Equal(LocalRedirect.Default, callback.Headers.Location!.OriginalString);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", ct);
        Assert.Equal("bob", me!.Username);
    }

    [Fact]
    public async Task OidcCallback_AllowsUserInAllowedGroup_FromIdToken()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        idp.IdTokenClaims["preferred_username"] = "carol";
        idp.IdTokenClaims["groups"] = new[] { "garagestack-users" };

        await using var factory = CreateOidcFactory(idp, settings =>
            settings["Oidc__AllowedGroups"] = "garagestack-users");
        using var client = CreateClient(factory);

        await SignInAsync(client, idp, "/", ct);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", ct);
        Assert.Equal("carol", me!.Username);
    }

    [Fact]
    public async Task OidcCallback_DeniesUserOutsideAllowedGroups()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        idp.UserInfoClaims["preferred_username"] = "mallory";
        idp.UserInfoClaims["groups"] = new[] { "photos" };

        await using var factory = CreateOidcFactory(idp, settings =>
            settings["Oidc__AllowedGroups"] = "garagestack-users");
        using var client = CreateClient(factory);

        var callback = await SignInAsync(client, idp, "/", ct);

        Assert.Equal("/login?error=access_denied", callback.Headers.Location!.OriginalString);

        var me = await client.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    // ── Session handling ──────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesTheSession_SoACopiedCookieStopsWorking()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        idp.UserInfoClaims["preferred_username"] = "dave";

        await using var factory = CreateOidcFactory(idp);
        using var client = CreateClient(factory);

        var callback = await SignInAsync(client, idp, "/", ct);
        var sessionCookie = ExtractSessionCookie(callback);

        var logout = await client.PostAsync("/api/auth/logout", content: null, ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // Replay the cookie captured before logout, the way a stolen copy would.
        using var replay = CreateClient(factory, handleCookies: false);
        replay.DefaultRequestHeaders.Add("Cookie", $"{AuthEndpoints.CookieName}={sessionCookie}");

        var me = await replay.GetAsync("/api/auth/me", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutSession_Returns401RatherThanRedirecting()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        await using var factory = CreateOidcFactory(idp);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/auth/me", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    // ── Sign-in method discovery ──────────────────────────────────────────────

    [Fact]
    public async Task AuthConfig_ReportsOidc_AndHidesPasswordLogin_WhenOidcIsConfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        await using var factory = CreateOidcFactory(idp, settings =>
        {
            settings["Oidc__ProviderName"] = "Authentik";
            settings["Oidc__AutoLogin"] = "true";
            // Configured, but OIDC takes precedence.
            settings["Auth__Username"] = DemoUsername;
            settings["Auth__Password"] = DemoPassword;
        });
        using var client = CreateClient(factory);

        var config = await client.GetFromJsonAsync<AuthConfigResponse>("/api/auth/config", ct);

        Assert.True(config!.OidcEnabled);
        Assert.True(config.OidcAutoLogin);
        Assert.Equal("Authentik", config.OidcProviderName);
        Assert.False(config.PasswordLoginEnabled);
    }

    [Fact]
    public async Task PasswordLogin_IsRejected_WhenOidcIsConfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        await using var factory = CreateOidcFactory(idp, settings =>
        {
            settings["Auth__Username"] = DemoUsername;
            settings["Auth__Password"] = DemoPassword;
        });
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PasswordLogin_WorksAlongsideOidc_WhenExplicitlyEnabled()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider();
        await using var factory = CreateOidcFactory(idp, settings =>
        {
            settings["Auth__Username"] = DemoUsername;
            settings["Auth__Password"] = DemoPassword;
            settings["Auth__PasswordLoginEnabled"] = "true";
        });
        using var client = CreateClient(factory);

        var config = await client.GetFromJsonAsync<AuthConfigResponse>("/api/auth/config", ct);
        Assert.True(config!.OidcEnabled);
        Assert.True(config.PasswordLoginEnabled);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", ct);
        Assert.Equal(DemoUsername, me!.Username);
    }

    // ── Password login fallback (no provider configured) ──────────────────────

    [Fact]
    public async Task AuthConfig_ReportsPasswordLogin_WhenNoProviderIsConfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);

        var config = await client.GetFromJsonAsync<AuthConfigResponse>("/api/auth/config", ct);

        Assert.True(config!.PasswordLoginEnabled);
        Assert.False(config.OidcEnabled);
        Assert.Null(config.OidcProviderName);
    }

    [Fact]
    public async Task PasswordLogin_SignsIn_WhenNoProviderIsConfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", ct);
        Assert.Equal(DemoUsername, me!.Username);
    }

    [Fact]
    public async Task PasswordLogin_WithWrongPassword_IsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = "wrong", rememberMe = false },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task OidcLogin_IsNotFound_WhenNoProviderIsConfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/auth/oidc/login", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks the whole sign-in: challenge, copy the nonce the handler generated into the fake
    /// provider so its id_token matches, then replay the provider's redirect back to the callback.
    /// </summary>
    private static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client, FakeIdentityProvider idp, string returnUrl, CancellationToken ct)
    {
        var challenge = await client.GetAsync(
            $"/api/auth/oidc/login?returnUrl={Uri.EscapeDataString(returnUrl)}", ct);

        var authorizeQuery = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        idp.Nonce = authorizeQuery["nonce"];

        var callbackUrl = QueryHelpers.AddQueryString(OidcOptions.CallbackPath, new Dictionary<string, string?>
        {
            ["code"] = "test-auth-code",
            ["state"] = authorizeQuery["state"],
        });

        return await client.GetAsync(callbackUrl, ct);
    }

    private static string ExtractSessionCookie(HttpResponseMessage response)
    {
        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith($"{AuthEndpoints.CookieName}=", StringComparison.Ordinal));

        return setCookie[$"{AuthEndpoints.CookieName}=".Length..].Split(';')[0];
    }

    private static HttpClient CreateClient(WebApplicationFactory<ApiEntryPoint> factory, bool handleCookies = true) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = handleCookies,
        });

    private static TestApiFactory CreateOidcFactory(
        FakeIdentityProvider idp, Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = BaseSettings();
        settings["Oidc__Authority"] = FakeIdentityProvider.IssuerUrl;
        settings["Oidc__ClientId"] = FakeIdentityProvider.ClientId;
        settings["Oidc__ClientSecret"] = FakeIdentityProvider.ClientSecret;
        configure?.Invoke(settings);

        return new TestApiFactory(settings, idp);
    }

    private static TestApiFactory CreatePasswordOnlyFactory()
    {
        var settings = BaseSettings();
        settings["Auth__Username"] = DemoUsername;
        settings["Auth__Password"] = DemoPassword;

        return new TestApiFactory(settings, idp: null);
    }

    /// <summary>
    /// Keys are in environment-variable form ("__" for ":") because that is how the factory
    /// feeds them to the app; a null value clears the variable for the duration of the test.
    /// </summary>
    private static Dictionary<string, string?> BaseSettings() => new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        // Per-request info logging from a host started for every test drowns the test output.
        ["Serilog__MinimumLevel__Default"] = "Warning",
        // No database, MQTT or SAIC gateway needed.
        ["DEMO_MODE"] = "true",
        // The test server speaks plain HTTP, so Secure cookies would never come back.
        ["Auth__CookieSecure"] = "false",
        ["Cors__Origins__0"] = "http://localhost",
        // Anything inherited from the developer's own environment would change the behaviour
        // under test.
        ["Oidc__Authority"] = null,
        ["Oidc__ClientId"] = null,
        ["Oidc__ClientSecret"] = null,
        ["Oidc__RedirectUri"] = null,
        ["Oidc__AllowedGroups"] = null,
        ["Oidc__AllowedEmails"] = null,
        ["Oidc__ProviderName"] = null,
        ["Oidc__AutoLogin"] = null,
        ["Auth__Username"] = null,
        ["Auth__Password"] = null,
        ["Auth__PasswordLoginEnabled"] = null,
        ["SAIC_USER"] = null,
        ["SAIC_PASSWORD"] = null,
    };

    private sealed class TestApiFactory(Dictionary<string, string?> environment, FakeIdentityProvider? idp)
        : WebApplicationFactory<ApiEntryPoint>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // WebApplicationFactory's configuration hooks only run once the host is being built,
            // but Program.cs reads DEMO_MODE and the connection string while it is still
            // assembling the builder. Environment variables are the only input in place that
            // early, so they are applied for the duration of host construction and restored
            // immediately after -- by then the configuration has been read into the host.
            var previous = environment.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);

            foreach (var (key, value) in environment)
                Environment.SetEnvironmentVariable(key, value);

            try
            {
                return base.CreateHost(builder);
            }
            finally
            {
                foreach (var (key, value) in previous)
                    Environment.SetEnvironmentVariable(key, value);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // WebApplicationFactory defaults the host to Development, which would switch off the
            // CSRF origin check and relax CORS -- not the pipeline these tests are about.
            builder.UseEnvironment("Production");

            if (idp is null)
                return;

            builder.ConfigureTestServices(services =>
                // Runs before the framework's own post-configure step, which is what turns a
                // preset Configuration into a static configuration manager (no discovery call)
                // and wraps the handler into the backchannel HttpClient.
                services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
                {
                    options.Configuration = idp.Configuration;
                    options.BackchannelHttpHandler = idp.CreateBackchannel();
                }));
        }
    }
}
