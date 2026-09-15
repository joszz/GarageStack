using System.Security.Claims;
using GarageStack.Api.Endpoints;
using GarageStack.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Serilog;

namespace GarageStack.Api.Authentication;

/// <summary>
/// Wires up the two ways into GarageStack: an OpenID Connect provider (preferred) and the
/// built-in password login (fallback when no provider is configured). Both end in the same
/// encrypted session cookie, so everything downstream -- endpoints, SignalR, logout revocation --
/// only ever deals with one kind of session.
/// </summary>
internal static class AuthenticationSetup
{
    /// <summary>Default OIDC session length. Overridable with AUTH_SESSION_LIFETIME_HOURS.</summary>
    private const double DefaultSessionLifetimeHours = 168;

    private const double MinSessionLifetimeHours = 1;
    private const double MaxSessionLifetimeHours = 8760;

    /// <summary>SPA route users land on when sign-in fails; ?error= drives the message shown.</summary>
    private const string LoginPagePath = "/login";

    /// <summary>Logger category shared by every sign-in related log line.</summary>
    internal const string LogCategory = "GarageStack.Authentication";

    internal static IServiceCollection AddGarageStackAuthentication(
        this IServiceCollection services,
        IConfiguration config,
        IWebHostEnvironment env)
    {
        var oidc = config.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();
        oidc.Validate();

        var passwordLogin = PasswordLoginOptions.Resolve(config, oidc.Enabled);

        if (!oidc.Enabled && !passwordLogin.Enabled)
            throw new InvalidOperationException(
                passwordLogin.Configured
                    ? "No authentication method is available: the password login is switched off by " +
                      "AUTH_PASSWORD_LOGIN_ENABLED and no identity provider is configured. Set it back to true, " +
                      "or set OIDC_AUTHORITY and OIDC_CLIENT_ID."
                    : "No authentication method is configured. Set OIDC_AUTHORITY and OIDC_CLIENT_ID to sign in " +
                      "with an identity provider, or set AUTH_USERNAME and AUTH_PASSWORD to use the built-in " +
                      "password login.");

        LogConfiguration(oidc, passwordLogin);

        services.AddSingleton(oidc);
        services.AddSingleton(passwordLogin);

        var securePolicy = ResolveCookieSecurePolicy(config, env);
        var sessionLifetime = ResolveSessionLifetime(config);

        var authentication = services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                // Challenging with the cookie scheme returns 401 to the SPA's fetch calls.
                // Redirecting to the provider only ever happens through /api/auth/oidc/login.
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options => ConfigureCookie(options, securePolicy, sessionLifetime));

        if (oidc.Enabled)
            authentication.AddOpenIdConnect(options => ConfigureOidc(options, oidc, securePolicy, sessionLifetime));

        services.AddAuthorization();

        return services;
    }

    private static void ConfigureCookie(
        CookieAuthenticationOptions options,
        CookieSecurePolicy securePolicy,
        TimeSpan sessionLifetime)
    {
        options.Cookie.Name = AuthEndpoints.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = securePolicy;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = sessionLifetime;
        // Fixed rather than sliding: the SPA caches the expiry it was told about, and a
        // server-side renewal it never hears about would leave the two disagreeing.
        options.SlidingExpiration = false;

        options.Events = new CookieAuthenticationEvents
        {
            // This is an API, not a server-rendered app: answer with status codes instead of
            // redirecting fetch() calls to a login page they cannot render.
            OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            // Cookies are bearer credentials too: without this check a copy captured before
            // logout would keep working until it expired on its own.
            OnValidatePrincipal = async ctx =>
            {
                var sessionId = ctx.Principal?.FindFirst(SessionPrincipal.SessionIdClaimType)?.Value;
                if (string.IsNullOrEmpty(sessionId))
                    return;

                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                if (!await TokenRevocation.IsRevokedAsync(db, sessionId, ctx.HttpContext.RequestAborted))
                    return;

                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            },
        };
    }

    private static void ConfigureOidc(
        OpenIdConnectOptions options,
        OidcOptions oidc,
        CookieSecurePolicy securePolicy,
        TimeSpan sessionLifetime)
    {
        options.Authority = oidc.Authority;
        options.ClientId = oidc.ClientId;
        options.ClientSecret = string.IsNullOrWhiteSpace(oidc.ClientSecret) ? null : oidc.ClientSecret;
        options.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
        options.CallbackPath = OidcOptions.CallbackPath;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        // Authorization code + PKCE, the flow every current provider recommends.
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;

        // form_post would return the response as a cross-site POST, which browsers only carry
        // SameSite=None cookies on -- and those must be Secure, breaking every plain-HTTP LAN
        // install. A redirect back with query parameters works with SameSite=Lax instead.
        options.ResponseMode = OpenIdConnectResponseMode.Query;

        // Nothing downstream calls the provider's APIs, so the tokens would only bloat the cookie.
        options.SaveTokens = false;

        // Providers differ in what they put in the id_token; some (Authelia 4.39+, for one) keep
        // it minimal and expect the userinfo endpoint to be used for the rest. The handler skips
        // this automatically when the provider advertises no userinfo endpoint.
        options.GetClaimsFromUserInfoEndpoint = true;

        // Keep the short OIDC claim names instead of the legacy SOAP-style URIs, so config like
        // OIDC_GROUPS_CLAIM means what the provider's own documentation says it means.
        options.MapInboundClaims = false;

        options.Scope.Clear();
        foreach (var scope in oidc.ScopeList)
            options.Scope.Add(scope);

        // Claims the handler does not map out of the userinfo response by default.
        options.ClaimActions.MapUniqueJsonKey("preferred_username", "preferred_username");
        options.ClaimActions.MapJsonKey(oidc.GroupsClaim, oidc.GroupsClaim);

        // Both are short-lived cookies that have to survive the redirect back from the provider,
        // which is cross-site: Strict would drop them and every login would fail correlation.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = securePolicy;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = securePolicy;

        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = ctx =>
            {
                // Behind more than one proxy the request no longer carries the public scheme,
                // host or port, so the derived redirect URI would not match what is registered.
                if (!string.IsNullOrWhiteSpace(oidc.RedirectUri))
                    ctx.ProtocolMessage.RedirectUri = oidc.RedirectUri;

                return Task.CompletedTask;
            },

            OnTicketReceived = ctx =>
            {
                var logger = CreateLogger(ctx.HttpContext);
                var principal = ctx.Principal ?? new ClaimsPrincipal();
                var displayName = SessionPrincipal.ResolveDisplayName(principal);

                var decision = OidcAccessPolicy.Evaluate(principal, oidc);
                if (!decision.Allowed)
                {
                    logger.LogWarning(
                        "OIDC sign-in denied for {User}: {Reason}", displayName, decision.Reason);
                    ctx.Response.Redirect(LoginPageUrl("access_denied"));
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                }

                ctx.Principal = SessionPrincipal.Create(
                    displayName,
                    SessionPrincipal.ResolveSubject(principal),
                    OpenIdConnectDefaults.AuthenticationScheme,
                    SessionPrincipal.NewSessionId());

                ctx.Properties!.IsPersistent = true;
                ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow.Add(sessionLifetime);

                logger.LogInformation("OIDC sign-in for {User} ({Reason})", displayName, decision.Reason);
                return Task.CompletedTask;
            },

            // The user cancelled at the provider, or the provider refused the application.
            OnAccessDenied = ctx =>
            {
                ctx.Response.Redirect(LoginPageUrl("access_denied"));
                ctx.HandleResponse();
                return Task.CompletedTask;
            },

            // Anything else: expired correlation cookie, clock skew, an unreachable provider.
            // Without this the exception handler would answer a browser navigation with JSON.
            OnRemoteFailure = ctx =>
            {
                CreateLogger(ctx.HttpContext).LogWarning(
                    ctx.Failure, "OIDC sign-in failed: {Message}", ctx.Failure?.Message);
                ctx.Response.Redirect(LoginPageUrl("oidc_failed"));
                ctx.HandleResponse();
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>Sends the browser back to the SPA login page with a code it can explain.</summary>
    internal static string LoginPageUrl(string error) => $"{LoginPagePath}?error={Uri.EscapeDataString(error)}";

    private static Microsoft.Extensions.Logging.ILogger CreateLogger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);

    private static CookieSecurePolicy ResolveCookieSecurePolicy(IConfiguration config, IWebHostEnvironment env) =>
        config.GetValue<bool?>("Auth:CookieSecure") switch
        {
            true => CookieSecurePolicy.Always,
            // Explicit false keeps plain-HTTP LAN installs working, which is why the Docker
            // defaults set it.
            false => CookieSecurePolicy.None,
            null => env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always,
        };

    private static TimeSpan ResolveSessionLifetime(IConfiguration config)
    {
        var hours = config.GetValue<double?>("Auth:SessionLifetimeHours") ?? DefaultSessionLifetimeHours;
        return TimeSpan.FromHours(Math.Clamp(hours, MinSessionLifetimeHours, MaxSessionLifetimeHours));
    }

    private static void LogConfiguration(OidcOptions oidc, PasswordLoginOptions passwordLogin)
    {
        if (oidc.Enabled)
        {
            Log.Information(
                "Authentication: OIDC via {Authority} (client {ClientId}, auto-login {AutoLogin})",
                oidc.Authority, oidc.ClientId, oidc.AutoLogin);

            if (!oidc.HasAccessRestrictions)
                Log.Warning(
                    "OIDC_ALLOWED_GROUPS and OIDC_ALLOWED_EMAILS are both empty -- every account your provider " +
                    "accepts can sign in to GarageStack and control the car. Restrict access at the provider, " +
                    "or set one of these variables.");

            if (passwordLogin.Enabled)
                Log.Warning(
                    "The built-in password login is enabled alongside OIDC (AUTH_PASSWORD_LOGIN_ENABLED=true). " +
                    "It is a second way in that your identity provider knows nothing about -- give it a strong, " +
                    "dedicated password via AUTH_USERNAME/AUTH_PASSWORD.");
            else if (passwordLogin.Configured)
                Log.Information(
                    "Password login is disabled because OIDC is configured. " +
                    "Set AUTH_PASSWORD_LOGIN_ENABLED=true to keep it available as well.");
        }
        else
        {
            Log.Information("Authentication: built-in password login (no OIDC provider configured)");
        }
    }
}
