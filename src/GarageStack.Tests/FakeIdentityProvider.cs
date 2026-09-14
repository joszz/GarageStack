using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GarageStack.Tests;

/// <summary>
/// A stand-in OpenID Connect provider for the sign-in flow tests: it hands the handler a static
/// discovery document (so no network call is made) and answers the token and userinfo calls over
/// an in-memory backchannel with a properly signed id_token.
/// </summary>
internal sealed class FakeIdentityProvider : IDisposable
{
    internal const string IssuerUrl = "https://idp.test";
    internal const string ClientId = "garagestack-test";
    internal const string ClientSecret = "test-client-secret";
    internal const string AccessToken = "fake-access-token";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _untrustedRsa = RSA.Create(2048);
    private bool _rejectPushedAuthorizationRequests;
    private readonly RsaSecurityKey _signingKey;
    private readonly RsaSecurityKey _untrustedSigningKey;

    internal FakeIdentityProvider()
    {
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "fake-idp-key" };
        // Never published in the discovery document, so anything signed with it must be refused.
        _untrustedSigningKey = new RsaSecurityKey(_untrustedRsa) { KeyId = "untrusted-key" };

        Configuration = new OpenIdConnectConfiguration
        {
            Issuer = IssuerUrl,
            AuthorizationEndpoint = $"{IssuerUrl}/authorize",
            TokenEndpoint = $"{IssuerUrl}/token",
            UserInfoEndpoint = $"{IssuerUrl}/userinfo",
        };
        Configuration.SigningKeys.Add(_signingKey);
    }

    internal OpenIdConnectConfiguration Configuration { get; }

    internal string Subject { get; set; } = "fake-subject-1";

    /// <summary>Copied from the authorize request by the test before the callback is replayed.</summary>
    internal string? Nonce { get; set; }

    /// <summary>Extra claims to put in the id_token, on top of sub/nonce.</summary>
    internal Dictionary<string, object> IdTokenClaims { get; } = [];

    /// <summary>Claims returned from the userinfo endpoint. "sub" is added automatically.</summary>
    internal Dictionary<string, object> UserInfoClaims { get; } = [];

    /// <summary>Raw body of the last token request, so tests can assert on PKCE and the code.</summary>
    internal string? LastTokenRequestBody { get; private set; }

    /// <summary>Signs the id_token with a key the discovery document does not list.</summary>
    internal bool SignWithUntrustedKey { get; set; }

    /// <summary>
    /// Advertises a pushed authorization request endpoint (as Authelia and Keycloak do) that
    /// answers every request with invalid_client, the way a provider does when the client
    /// secret or its authentication method is wrong.
    /// </summary>
    internal bool RejectPushedAuthorizationRequests
    {
        get => _rejectPushedAuthorizationRequests;
        set
        {
            _rejectPushedAuthorizationRequests = value;
            Configuration.PushedAuthorizationRequestEndpoint = value ? $"{IssuerUrl}/par" : null;
        }
    }

    internal HttpMessageHandler CreateBackchannel() => new BackchannelHandler(this);

    private string CreateIdToken()
    {
        var claims = new Dictionary<string, object>(IdTokenClaims)
        {
            ["sub"] = Subject,
        };

        if (!string.IsNullOrEmpty(Nonce))
            claims["nonce"] = Nonce;

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = IssuerUrl,
            Audience = ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                SignWithUntrustedKey ? _untrustedSigningKey : _signingKey,
                SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public void Dispose()
    {
        _rsa.Dispose();
        _untrustedRsa.Dispose();
    }

    private sealed class BackchannelHandler(FakeIdentityProvider provider) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.EndsWith("/par", StringComparison.Ordinal))
            {
                var error = Json(new Dictionary<string, object>
                {
                    ["error"] = "invalid_client",
                    ["error_description"] = "Client authentication failed.",
                });
                error.StatusCode = HttpStatusCode.Unauthorized;
                return error;
            }

            if (path.EndsWith("/token", StringComparison.Ordinal))
            {
                provider.LastTokenRequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                return Json(new Dictionary<string, object>
                {
                    ["token_type"] = "Bearer",
                    ["access_token"] = AccessToken,
                    ["expires_in"] = 3600,
                    ["id_token"] = provider.CreateIdToken(),
                });
            }

            if (path.EndsWith("/userinfo", StringComparison.Ordinal))
            {
                var claims = new Dictionary<string, object>(provider.UserInfoClaims)
                {
                    ["sub"] = provider.Subject,
                };

                return Json(claims);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(Dictionary<string, object> payload) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
    }
}
