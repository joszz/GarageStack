using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GarageStack.Api;
using GarageStack.Api.Authentication;
using GarageStack.Api.Endpoints;
using GarageStack.Api.Services;
using GarageStack.Core.Configuration;
using GarageStack.Core.Models;
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

    // An uptime monitor has no session: readiness answers without one, and in demo mode it checks
    // the in-memory database only, since there is no broker and no Worker.
    [Fact]
    public async Task Readiness_AnswersWithoutSignIn_NamingEachCheck()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync(HealthEndpoints.ReadinessPath, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"status":"healthy","checks":{"database":"healthy"}}""",
            await response.Content.ReadAsStringAsync(ct));
    }

    // Through the whole stack: setting home and work labels an unclassified demo trip between them
    // straight away, a second home is refused, and removing them takes the label off again. Places are removed at the end,
    // since the demo database is shared by every host in this run.
    [Fact]
    public async Task CommutePlaces_LabelTheTripBetweenThem_AndRemovingThemUndoesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var vehicles = await client.GetFromJsonAsync<JsonElement>("/api/vehicles", ct);
        var vin = vehicles[0].GetProperty("vin").GetString();
        // The demo ZS EV takes a charge target, so its list entry says so.
        Assert.True(vehicles[0].GetProperty("supportsTargetSoc").GetBoolean());
        var logUrl = $"/api/vehicles/{vin}/trips/log?from={DateTimeOffset.UtcNow.AddDays(-60):O}&to={DateTimeOffset.UtcNow:O}"
            .Replace("+", "%2B", StringComparison.Ordinal);
        async Task<TripLogEntry> EntryAsync(long id) =>
            (await client.GetFromJsonAsync<List<TripLogEntry>>(logUrl, ct))!.Single(e => e.Id == id);

        var open = (await client.GetFromJsonAsync<List<TripLogEntry>>(logUrl, ct))!.First(e => e.Purpose is null);
        var placesUrl = $"/api/vehicles/{vin}/commute-places";

        var home = await (await client.PostAsJsonAsync(placesUrl,
            new { kind = "home", latitude = open.StartLatitude, longitude = open.StartLongitude }, ct))
            .Content.ReadFromJsonAsync<CommutePlaceChange>(ct);
        var work = await (await client.PostAsJsonAsync(placesUrl,
            new { kind = "work", label = "Office", latitude = open.EndLatitude, longitude = open.EndLongitude, radiusMeters = 300 }, ct))
            .Content.ReadFromJsonAsync<CommutePlaceChange>(ct);
        var secondHome = await client.PostAsJsonAsync(placesUrl,
            new { kind = "home", latitude = open.EndLatitude, longitude = open.EndLongitude }, ct);

        Assert.Equal(0, home!.Marked);
        Assert.True(work!.Marked >= 1);
        Assert.Equal(HttpStatusCode.BadRequest, secondHome.StatusCode);
        Assert.Equal("commutePlace.homeExists", await ProblemCodeAsync(secondHome, ct));
        Assert.Equal(new CommutePlaceDto(work.Place!.Id, CommutePlaceKind.Work, "Office", open.EndLatitude, open.EndLongitude, 300), work.Place);
        var labelled = await EntryAsync(open.Id);
        Assert.Equal(TripPurpose.Commute, labelled.Purpose);
        Assert.True(labelled.PurposeIsAutomatic);

        await client.DeleteAsync($"{placesUrl}/{home.Place!.Id}", ct);
        var removed = await (await client.DeleteAsync($"{placesUrl}/{work.Place.Id}", ct))
            .Content.ReadFromJsonAsync<CommutePlaceChange>(ct);

        Assert.Null(removed!.Place);
        Assert.Null((await EntryAsync(open.Id)).Purpose);
        Assert.Empty((await client.GetFromJsonAsync<List<CommutePlaceDto>>(placesUrl, ct))!);
    }

    // The statistics page asks for its period by local day, however long it is.
    [Fact]
    public async Task DailyStats_ServeEveryDayOfThePeriod_InTheBrowsersZone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var vin = (await client.GetFromJsonAsync<JsonElement>("/api/vehicles", ct))[0].GetProperty("vin").GetString();

        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var from = today.AddDays(-399);
        var stats = await client.GetFromJsonAsync<List<DailyStat>>(
            $"/api/vehicles/{vin}/stats/daily?from={from:yyyy-MM-dd}&to={today:yyyy-MM-dd}&timeZone=Europe/Amsterdam", ct);

        Assert.Equal(400, stats!.Count);
        Assert.Equal(from, stats[0].Date);
        // The demo car reports a month of history, so recent days carry figures.
        Assert.Contains(stats, s => s.EvSocPercent is not null);

        var unknownZone = await client.GetAsync($"/api/vehicles/{vin}/stats/daily?timeZone=Mars/Olympus_Mons", ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknownZone.StatusCode);
        Assert.Contains("stats.timeZoneUnknown", await unknownZone.Content.ReadAsStringAsync(ct));

        // The raw history it replaces is gone.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{vin}/history", ct)).StatusCode);
    }

    [Fact]
    public async Task UpdateNotice_NeedsASignedInUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var anonymous = CreateClient(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/updates", ct)).StatusCode);

        using var client = CreateClient(factory);
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // The test host never asks GitHub, so there is nothing to offer.
        var update = await client.GetFromJsonAsync<UpdateInfo>("/api/updates", ct);
        Assert.Equal(UpdateInfo.None, update);
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
    public async Task PasswordLogin_TakesItsCredentialsFromTheDeploymentsVariables()
    {
        // AUTH_USERNAME and AUTH_PASSWORD as compose and the Unraid template pass them, with no
        // Auth__Username form anywhere: the host reads the deployment's own names.
        var ct = TestContext.Current.CancellationToken;
        var settings = BaseSettings();
        settings["AUTH_USERNAME"] = DemoUsername;
        settings["AUTH_PASSWORD"] = DemoPassword;
        await using var factory = new TestApiFactory(settings, idp: null);
        using var client = CreateClient(factory);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
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

    // ── Error answers ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ForeignOrigin_IsRefusedWithACodedProblem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Origin", "https://evil.example.com");

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("csrf.originNotAllowed", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(403, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task InvalidRequest_IsRefusedWithACodedProblem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);

        var response = await client.PostAsJsonAsync("/api/map/reverse", new { points = Array.Empty<object>() }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("map.pointsRequired", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("points must contain at least one coordinate", problem.RootElement.GetProperty("detail").GetString());
    }

    // The Docker default: the session cookie is Secure exactly when the browser came in over HTTPS,
    // so a TLS proxy gets the flag and a plain-HTTP LAN install can still sign in.
    [Theory]
    [InlineData("https://localhost", true)]
    [InlineData("http://localhost", false)]
    public async Task AutoCookieSecurity_FollowsTheRequestScheme(string baseAddress, bool secure)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory(s => s["Auth__CookieSecure"] = "auto");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri(baseAddress),
        });

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var sessionCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Equal(secure, sessionCookie.Contains("; secure", StringComparison.OrdinalIgnoreCase));
    }

    // Browser errors reach the API's log, but only from a signed-in page.
    [Fact]
    public async Task ClientErrorReport_IsTakenFromASignedInPageOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        var report = new { message = "TypeError: x is undefined", stack = "at a (index.js:1:2)", page = "/map", source = "index.js:1:2" };

        var anonymous = await client.PostAsJsonAsync("/api/client-errors", report, ct);
        await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);
        var signedIn = await client.PostAsJsonAsync("/api/client-errors", report, ct);
        var empty = await client.PostAsJsonAsync("/api/client-errors", new { message = " " }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    // ── Endpoints end to end ──────────────────────────────────────────────────

    private const string DemoVin = "DEMOVIN0000012345";

    // An item's whole life: added, listed, a service logged against it, removed with its log.
    // The demo database is shared by every host in this class, so the item has a name of its own.
    [Fact]
    public async Task MaintenanceItem_CanBeAddedLoggedAndRemoved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);
        var items = $"/api/vehicles/{DemoVin}/maintenance";
        var name = $"Brake fluid {Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync(items, new { name, intervalMonths = 24 }, ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetInt32();
        var logged = await client.PostAsJsonAsync(
            $"{items}/{id}/log", new { performedAt = DateTime.UtcNow.AddDays(-1), odometerKm = 1200, notes = "replaced" }, ct);
        var log = await client.GetFromJsonAsync<JsonElement>($"{items}/{id}/log", ct);
        var deleted = await client.DeleteAsync($"{items}/{id}", ct);
        var after = await client.GetFromJsonAsync<JsonElement>(items, ct);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, logged.StatusCode);
        Assert.Equal("replaced", Assert.Single(log.EnumerateArray()).GetProperty("notes").GetString());
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.DoesNotContain(after.EnumerateArray(), item => item.GetProperty("name").GetString() == name);
    }

    [Theory]
    [InlineData("""{"name":"","intervalKm":10000}""", "maintenance.nameRequired")]
    [InlineData("""{"name":"Oil"}""", "maintenance.intervalRequired")]
    [InlineData("""{"name":"Oil","intervalMonths":121}""", "maintenance.intervalMonthsOutOfRange")]
    public async Task MaintenanceItem_IsRefusedWithTheRuleItBreaks(string body, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);

        var response = await client.PostAsync(
            $"/api/vehicles/{DemoVin}/maintenance",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemCodeAsync(response, ct));
    }

    [Theory]
    [InlineData("lock", """{"value":"maybe"}""", "command.invalidValue")]
    [InlineData("fly", """{"value":"now"}""", "command.unknown")]
    [InlineData("lock", "{}", "command.valueMissing")]
    [InlineData("lock", """{"value":1}""", "command.valueNotText")]
    [InlineData("lock", """{"value":" "}""", "command.valueEmpty")]
    public async Task Command_IsRefusedWithTheReason(string command, string body, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);

        var response = await client.PostAsync(
            $"/api/vehicles/{DemoVin}/commands/{command}",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemCodeAsync(response, ct));
    }

    [Fact]
    public async Task Command_IsSentOnTheGatewaysTopic()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);

        var response = await client.PostAsJsonAsync($"/api/vehicles/{DemoVin}/commands/lock", new { value = "True" }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.EndsWith($"/vehicles/{DemoVin}/doors/locked/set", sent.GetProperty("topic").GetString());
    }

    [Fact]
    public async Task Notifications_ListAndRefuseAnUnknownOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);

        var list = await client.GetAsync("/api/notifications", ct);
        var archive = await client.PatchAsync("/api/notifications/987654/archive", null, ct);
        var delete = await client.DeleteAsync("/api/notifications/987654", ct);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await list.Content.ReadFromJsonAsync<JsonElement>(ct)).ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, archive.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    // Unsubscribing an endpoint the server never had is not an error: the browser may be cleaning up.
    [Fact]
    public async Task PushUnsubscribe_OfAnUnknownEndpoint_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);

        var response = await client.PostAsJsonAsync("/api/push/unsubscribe", new { endpoint = "https://push.example/unknown" }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The widget has no session: its key is the whole of its protection.
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("wrong-key", HttpStatusCode.Unauthorized)]
    [InlineData("widget-test-key", HttpStatusCode.OK)]
    public async Task Widget_AnswersOnlyTheConfiguredKey(string? key, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory(s => s["Widget__ApiKey"] = "widget-test-key");
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/widget/{DemoVin}/status");
        if (key is not null) request.Headers.Add("X-Widget-Key", key);

        var response = await client.SendAsync(request, ct);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Widget_WithoutAConfiguredKey_SaysItIsSwitchedOff()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/widget/{DemoVin}/status");
        request.Headers.Add("X-Widget-Key", "anything");

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("widget.notConfigured", await ProblemCodeAsync(response, ct));
    }

    // NaN binds from the query string and slips past range comparisons; it once reached the tile
    // maths as tile (0,0) and was fetched and cached upstream.
    [Theory]
    [InlineData("/api/map/charging-stations?lat=NaN&lng=5&distanceKm=10&minPowerKw=0&maxPowerKw=350", "map.coordinatesOutOfRange")]
    [InlineData("/api/map/poi?type=fuel&vehicleType=hev&lat=52&lng=Infinity&radiusKm=10", "map.coordinatesOutOfRange")]
    [InlineData("/api/map/poi?type=fuel&vehicleType=hev&lat=52&lng=5&radiusKm=NaN", "map.radiusOutOfRange")]
    public async Task MapQuery_WithANonFiniteNumber_IsRefused(string url, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);

        var response = await client.GetAsync(url, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemCodeAsync(response, ct));
    }

    // ── Rate limiting ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GlobalRateLimit_RejectsRequests_PastTheConfiguredBudget()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory(
            settings => settings["RateLimits__GlobalPerMinute"] = "1");
        using var client = CreateClient(factory);

        var allowed = await client.GetAsync("/api/auth/config", ct);
        var rejected = await client.GetAsync("/api/auth/config", ct);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    // Ten tries per five minutes per address: enough for typos, too few to guess a password.
    [Fact]
    public async Task LoginRateLimit_RefusesTheEleventhTry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);

        var answers = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 11; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login", new { username = DemoUsername, password = "wrong", rememberMe = false }, ct);
            answers.Add(response.StatusCode);
        }

        Assert.All(answers.Take(10), status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, answers[10]);
    }

    [Fact]
    public async Task GlobalRateLimit_FailsStartup_WhenTheBudgetLocksEveryoneOut()
    {
        await using var factory = CreatePasswordOnlyFactory(
            settings => settings["RateLimits__GlobalPerMinute"] = "0");

        // The entry point logs the reason and exits, so the host never reaches the test server:
        // a budget that would reject every request stops the app instead of silently breaking it.
        Assert.Throws<InvalidOperationException>(() => CreateClient(factory));
    }

    // ── Settings kept for the account ─────────────────────────────────────────

    // The demo database outlives each host, so every test saves values of its own rather than
    // counting on an account that has never saved anything.

    [Fact]
    public async Task Settings_FollowTheAccountToItsOtherDevices_KeyByKey()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var laptop = CreateClient(factory);
        using var phone = CreateClient(factory);
        await PasswordLoginAsync(laptop, ct);
        await PasswordLoginAsync(phone, ct);
        var theme = $"theme-{Guid.NewGuid():N}";
        var locale = $"locale-{Guid.NewGuid():N}";

        var laptopSave = await laptop.PatchAsJsonAsync("/api/settings/ui", new { theme }, ct);
        var phoneSave = await phone.PatchAsJsonAsync("/api/settings/ui", new { locale }, ct);

        Assert.Equal(HttpStatusCode.NoContent, laptopSave.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, phoneSave.StatusCode);
        // Each device saved one key, and neither save dropped the other's.
        Assert.Equal(theme, await SavedSettingAsync(laptop, "ui", "theme", ct));
        Assert.Equal(locale, await SavedSettingAsync(laptop, "ui", "locale", ct));
    }

    [Fact]
    public async Task Settings_OfAProviderAccount_StayApartFromAPasswordAccountOfTheSameName()
    {
        var ct = TestContext.Current.CancellationToken;
        using var idp = new FakeIdentityProvider { Subject = DemoUsername };
        await using var factory = CreateOidcFactory(idp, settings =>
        {
            settings["Auth__Username"] = DemoUsername;
            settings["Auth__Password"] = DemoPassword;
            settings["Auth__PasswordLoginEnabled"] = "true";
        });
        using var provider = CreateClient(factory);
        using var password = CreateClient(factory);
        await SignInAsync(provider, idp, "/", ct);
        await PasswordLoginAsync(password, ct);
        var theme = $"theme-{Guid.NewGuid():N}";

        var save = await provider.PatchAsJsonAsync("/api/settings/ui", new { theme }, ct);

        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
        Assert.Equal(theme, await SavedSettingAsync(provider, "ui", "theme", ct));
        Assert.NotEqual(theme, await SavedSettingAsync(password, "ui", "theme", ct));
    }

    [Fact]
    public async Task Settings_RefuseVisitorsAndUnknownSections()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var visitor = CreateClient(factory);
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);

        var anonymous = await visitor.GetAsync("/api/settings", ct);
        var unknown = await client.PatchAsJsonAsync("/api/settings/secrets", new { theme = "light" }, ct);
        var notAnObject = await client.PatchAsJsonAsync("/api/settings/ui", new[] { "light" }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("settings.unknownSection", await ProblemCodeAsync(unknown, ct));
        Assert.Equal(HttpStatusCode.BadRequest, notAnObject.StatusCode);
        Assert.Equal("settings.notAnObject", await ProblemCodeAsync(notAnObject, ct));
    }

    [Fact]
    public async Task ClimateSchedules_RoundTripThroughTheApi()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);
        const string schedules = "/api/vehicles/DEMOVIN0000012345/climate-schedules";
        var request = new
        {
            name = (string?)null,
            enabled = true,
            startTime = "06:45",
            days = new[] { 6, 7 },
            timeZoneId = "Europe/Amsterdam",
            mode = "front",
            temperatureC = 21,
            rearDefroster = true,
            seatLeftLevel = 0,
            seatRightLevel = 0,
            onlyBelowC = 2.5,
            onlyAboveC = (double?)null,
        };

        var created = await client.PostAsJsonAsync(schedules, request, ct);
        using var body = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct));
        var schedule = body.RootElement;
        var id = schedule.GetProperty("id").GetInt32();
        var switchedOff = await client.PutAsJsonAsync($"{schedules}/{id}", request with { enabled = false }, ct);
        using var off = System.Text.Json.JsonDocument.Parse(await switchedOff.Content.ReadAsStringAsync(ct));
        var numericMode = await client.PostAsJsonAsync(schedules, new { request.startTime, request.timeZoneId, mode = 2, temperatureC = 21 }, ct);
        var deleted = await client.DeleteAsync($"{schedules}/{id}", ct);
        var deletedAgain = await client.DeleteAsync($"{schedules}/{id}", ct);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("front", schedule.GetProperty("mode").GetString());
        Assert.Equal("06:45", schedule.GetProperty("startTime").GetString());
        Assert.EndsWith("Z", schedule.GetProperty("nextRunUtc").GetString());
        Assert.Equal(HttpStatusCode.OK, switchedOff.StatusCode);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, off.RootElement.GetProperty("nextRunUtc").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, numericMode.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedAgain.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task PasswordLoginAsync(HttpClient client, CancellationToken ct)
    {
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = DemoUsername, password = DemoPassword, rememberMe = false },
            ct);
        login.EnsureSuccessStatusCode();
    }

    private static async Task<string?> SavedSettingAsync(HttpClient client, string section, string key, CancellationToken ct)
    {
        var sections = await client.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/settings", ct);
        return sections!.TryGetValue(section, out var saved) && saved.TryGetProperty(key, out var value)
            ? value.GetString()
            : null;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return problem.RootElement.GetProperty("code").GetString();
    }

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

    private static TestApiFactory CreatePasswordOnlyFactory(Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = BaseSettings();
        settings["Auth__Username"] = DemoUsername;
        settings["Auth__Password"] = DemoPassword;
        configure?.Invoke(settings);

        return new TestApiFactory(settings, idp: null);
    }

    /// <summary>
    /// Keys are in environment-variable form ("__" for ":") because that is how the factory
    /// feeds them to the app; a null value clears the variable for the duration of the test.
    /// </summary>
    private static Dictionary<string, string?> BaseSettings()
    {
        var settings = BaseSettingsByKey();

        // The deployment's own names (AUTH_USERNAME, OIDC_AUTHORITY, ...) fill the same settings,
        // so one set in the developer's shell would change the behaviour under test as well.
        foreach (var variable in EnvironmentAliases.Map.Keys)
            settings[variable] = null;

        return settings;
    }

    private static Dictionary<string, string?> BaseSettingsByKey() => new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        // Per-request info logging from a host started for every test drowns the test output.
        ["Serilog__MinimumLevel__Default"] = "Warning",
        // No database, MQTT or SAIC gateway needed.
        ["DEMO_MODE"] = "true",
        // A test never asks GitHub for the latest release.
        ["UpdateCheck__Enabled"] = "false",
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
