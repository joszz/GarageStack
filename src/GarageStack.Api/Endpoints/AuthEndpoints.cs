using System.Security.Cryptography;
using System.Text;
using GarageStack.Api.Authentication;
using GarageStack.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace GarageStack.Api.Endpoints;

public static class AuthEndpoints
{
    public const string CookieName = "garagestack-auth";

    private static readonly TimeSpan PasswordSessionLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan PasswordRememberMeLifetime = TimeSpan.FromDays(30);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Resolved once here instead of taken as handler parameters: both are fixed at startup
        // from configuration. Static analysis (CodeQL) treats every handler parameter as request
        // input, which would turn each check on these settings into a false alarm.
        var oidc = app.ServiceProvider.GetRequiredService<OidcOptions>();
        var passwordLogin = app.ServiceProvider.GetRequiredService<PasswordLoginOptions>();
        var logger = app.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(AuthenticationSetup.LogCategory);

        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        // Public on purpose: the login page has to know which sign-in methods exist before
        // anyone is authenticated. It exposes no secrets, only which buttons to render.
        group.MapGet("/config", () =>
            Results.Ok(new AuthConfigResponse(
                PasswordLoginEnabled: passwordLogin.Enabled,
                OidcEnabled: oidc.Enabled,
                OidcProviderName: oidc.Enabled ? oidc.ProviderName : null,
                OidcAutoLogin: oidc.Enabled && oidc.AutoLogin)))
        .WithSummary("Get the enabled sign-in methods");

        // A browser navigation, not a fetch: the response is a redirect to the identity
        // provider, and the provider redirects back to OidcOptions.CallbackPath.
        group.MapGet("/oidc/login", async (string? returnUrl, HttpContext httpContext) =>
        {
            if (!oidc.Enabled)
                return Results.NotFound();

            var properties = new AuthenticationProperties { RedirectUri = LocalRedirect.Sanitize(returnUrl) };

            try
            {
                await httpContext.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
                return Results.Empty;
            }
            catch (Exception ex) when (!httpContext.Response.HasStarted)
            {
                // The challenge talks to the provider before it redirects anywhere (fetching the
                // discovery document, and pushing the authorization request when the provider
                // supports PAR), so bad client credentials or an unreachable provider surface
                // here. This is a browser navigation: answer with the login page carrying an
                // error, not a JSON 500 the user can do nothing with.
                logger.LogError(ex, "Could not start OIDC sign-in with {Authority}", oidc.Authority);

                return Results.Redirect(AuthenticationSetup.LoginPageUrl("oidc_failed"));
            }
        })
        .RequireRateLimiting("oidc-login")
        .WithSummary("Start the OpenID Connect sign-in flow");

        group.MapPost("/logout", async (HttpContext httpContext, AppDbContext db, CancellationToken ct) =>
        {
            // Revoke the session server-side so a cookie copied before logout (from a
            // compromised device, say) stops working immediately instead of at its own expiry.
            // Authentication is not required: with no session there is simply nothing to revoke.
            var result = await httpContext.AuthenticateAsync();
            var sessionId = result.Principal?.FindFirst(SessionPrincipal.SessionIdClaimType)?.Value;

            if (!string.IsNullOrEmpty(sessionId))
            {
                var expiresAtUtc = result.Properties?.ExpiresUtc?.UtcDateTime
                    ?? DateTime.UtcNow.Add(PasswordRememberMeLifetime);
                await TokenRevocation.RevokeAsync(db, sessionId, expiresAtUtc, ct);
            }

            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return Results.NoContent();
        })
        .WithSummary("Clear the session cookie and revoke the session server-side");

        group.MapGet("/me", async (HttpContext httpContext) =>
        {
            var result = await httpContext.AuthenticateAsync();
            if (!result.Succeeded || result.Principal is null)
                return Results.Unauthorized();

            var username = SessionPrincipal.ResolveDisplayName(result.Principal);
            return Results.Ok(new MeResponse(username, result.Properties?.ExpiresUtc?.UtcDateTime));
        })
        .RequireAuthorization()
        .WithSummary("Get current authenticated user");

        group.MapPost("/login", async (LoginRequest req, HttpContext httpContext) =>
        {
            if (!passwordLogin.Enabled)
            {
                logger.LogWarning(
                    "Password login attempt while disabled (configured: {Configured})", passwordLogin.Configured);
                return Results.NotFound();
            }

            var providedUsername = req.Username?.Trim() ?? string.Empty;
            var providedPassword = req.Password ?? string.Empty;

            var validUser = FixedTimeEquals(providedUsername, passwordLogin.Username);
            var validPassword = FixedTimeEquals(providedPassword, passwordLogin.Password);

            if (!validUser || !validPassword)
            {
                logger.LogWarning(
                    "Failed login attempt for username={Username} from IP={RemoteIp}",
                    SanitizeForLog(providedUsername), httpContext.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }

            var expires = DateTimeOffset.UtcNow.Add(
                req.RememberMe ? PasswordRememberMeLifetime : PasswordSessionLifetime);

            var principal = SessionPrincipal.Create(
                providedUsername,
                providedUsername,
                CookieAuthenticationDefaults.AuthenticationScheme,
                SessionPrincipal.NewSessionId());

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = expires });

            return Results.Ok(new LoginResponse(providedUsername, expires.UtcDateTime));
        })
        .RequireRateLimiting("login")
        .WithSummary("Authenticate with the built-in password login");

        return app;
    }

    private static string SanitizeForLog(string value) =>
        value.Replace("\r", string.Empty).Replace("\n", string.Empty);

    internal static bool FixedTimeEquals(string left, string right)
    {
        // Hash both values first so the compared buffers always have identical length.
        var leftBytes = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        var rightBytes = SHA256.HashData(Encoding.UTF8.GetBytes(right));

        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public sealed record LoginRequest(string Username, string Password, bool RememberMe = false);
public sealed record LoginResponse(string Username, DateTime ExpiresAtUtc);
public sealed record MeResponse(string Username, DateTime? ExpiresAtUtc);
public sealed record AuthConfigResponse(
    bool PasswordLoginEnabled,
    bool OidcEnabled,
    string? OidcProviderName,
    bool OidcAutoLogin);
