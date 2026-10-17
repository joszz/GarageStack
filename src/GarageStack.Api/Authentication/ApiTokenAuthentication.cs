using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using GarageStack.Api.Endpoints;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GarageStack.Api.Authentication;

/// <summary>
/// Read-only API tokens: "Authorization: Bearer gs_..." reads GarageStack's data as the account that
/// made the token, without a session. A token reads and nothing more: it is refused for any request
/// that changes something, for managing tokens, and for the full export and the server logs.
/// </summary>
internal static class ApiTokenAuthentication
{
    public const string Scheme = "ApiToken";

    /// <summary>Picks the token scheme for a request that carries a GarageStack token, the session cookie otherwise.</summary>
    public const string SessionOrTokenScheme = "SessionOrApiToken";

    private const string TokenPrefix = "gs_";
    private const string BearerPrefix = "Bearer ";

    // Last use is kept to the minute, so a script polling every few seconds is not a write each time.
    private static readonly TimeSpan UsageResolution = TimeSpan.FromMinutes(1);

    // Reads a token must not do either: the export is every trip and every position the car ever
    // reported, and the logs are the services' own diagnostics. A leaked token meant for a dashboard
    // sensor would otherwise hand over the whole history.
    private static readonly string[] SessionOnlyReads = [ExportEndpoints.Path, LogEndpoints.Path];

    /// <summary>
    /// Whether the request carries a GarageStack token. Only "Bearer gs_..." counts: a proxy in front
    /// that passes its own bearer token (oauth2-proxy and similar forward-auth setups) must not push
    /// a request that also has a valid session cookie onto the token scheme, where it would fail.
    /// </summary>
    public static bool HasBearerToken(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith(BearerPrefix + TokenPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>A new token, and what is kept of it: its hash and its first characters.</summary>
    public static (string Token, string Hash, string Prefix) Create()
    {
        var token = TokenPrefix + Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token), token[..ApiTokenLimits.PrefixLength]);
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    internal sealed class Handler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiTokenRepository tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

            var presented = header[BearerPrefix.Length..].Trim();
            if (!presented.StartsWith(TokenPrefix, StringComparison.Ordinal))
                return AuthenticateResult.Fail("Not a GarageStack API token");

            var token = await tokens.FindByHashAsync(Hash(presented), Context.RequestAborted);
            if (token is null) return AuthenticateResult.Fail("Unknown or revoked API token");

            var now = DateTime.UtcNow;
            if (token.LastUsedAt is not { } lastUsed || now - lastUsed >= UsageResolution)
                await MarkUsedAsync(token.Id, now);

            var identity = new ClaimsIdentity(
                [
                    new Claim(SessionPrincipal.AccountKeyClaimType, token.AccountKey),
                    new Claim(ClaimTypes.Name, token.Name),
                ],
                Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }

        // Last use is a convenience shown in the token list. A database hiccup while noting it must
        // not turn the read the token was sent for into a 500.
        private async Task MarkUsedAsync(int id, DateTime now)
        {
            try
            {
                await tokens.MarkUsedAsync(id, now, Context.RequestAborted);
            }
            catch (Exception ex) when (!Context.RequestAborted.IsCancellationRequested)
            {
                Logger.LogWarning(ex, "Could not note the last use of API token {TokenId}", id);
            }
        }
    }

    /// <summary>
    /// Holds a token to reading: a request signed in with one may only GET, and never reach the
    /// token endpoints, the export or the logs, so a leaked token cannot send commands, change
    /// settings, make more tokens or carry off the car's whole history.
    /// </summary>
    internal sealed class ReadOnlyMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            if (context.User.Identity is { IsAuthenticated: true, AuthenticationType: Scheme })
            {
                var path = context.Request.Path;
                var refusal =
                    !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
                        ? ApiProblems.Problem(StatusCodes.Status403Forbidden, "token.readOnly",
                            "An API token can only read; sign in to change anything")
                        : path.StartsWithSegments(ApiTokenEndpoints.Path)
                            ? ApiProblems.Problem(StatusCodes.Status403Forbidden, "token.cannotManageTokens",
                                "API tokens are managed from a signed-in session only")
                            : SessionOnlyReads.Any(p => path.StartsWithSegments(p))
                                ? ApiProblems.Problem(StatusCodes.Status403Forbidden, "token.sessionOnly",
                                    "The export and the logs need a signed-in session; an API token cannot read them")
                                : null;
                if (refusal is not null)
                {
                    await refusal.ExecuteAsync(context);
                    return;
                }
            }

            await next(context);
        }
    }
}
