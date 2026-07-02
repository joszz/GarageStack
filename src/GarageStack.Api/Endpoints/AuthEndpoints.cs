using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace GarageStack.Api.Endpoints;

public static class AuthEndpoints
{
    public const string CookieName = "garagestack-auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/logout", (HttpContext httpContext) =>
        {
            httpContext.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
            return Results.NoContent();
        })
        .WithSummary("Clear the authentication cookie");

        group.MapGet("/me", (HttpContext httpContext) =>
        {
            var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value
                ?? httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

            DateTime? expiresAtUtc = null;
            var expClaim = httpContext.User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
            if (long.TryParse(expClaim, out var expUnix))
                expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;

            return Results.Ok(new MeResponse(username, expiresAtUtc));
        })
        .RequireAuthorization()
        .WithSummary("Get current authenticated user");

        group.MapPost("/login", (LoginRequest req, IConfiguration config, HttpContext httpContext, IWebHostEnvironment env, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AuthLogin");

            var configuredUsername = FirstNonEmpty(
                config["Auth:Username"],
                config["SAIC_USER"],
                config["Saic:User"]);
            var configuredPassword = FirstNonEmpty(
                config["Auth:Password"],
                config["SAIC_PASSWORD"],
                config["Saic:Password"]);

            if (string.IsNullOrWhiteSpace(configuredUsername) || string.IsNullOrWhiteSpace(configuredPassword))
            {
                logger.LogWarning(
                    "Auth credentials are not configured. Presence: Auth:Username={AuthUserSet}, SAIC_USER={SaicUserSet}, Auth:Password={AuthPassSet}, SAIC_PASSWORD={SaicPassSet}",
                    !string.IsNullOrWhiteSpace(config["Auth:Username"]),
                    !string.IsNullOrWhiteSpace(config["SAIC_USER"]),
                    !string.IsNullOrWhiteSpace(config["Auth:Password"]),
                    !string.IsNullOrWhiteSpace(config["SAIC_PASSWORD"]));
                return Results.Unauthorized();
            }

            var providedUsername = req.Username?.Trim() ?? string.Empty;
            var providedPassword = req.Password ?? string.Empty;

            var validUser = FixedTimeEquals(providedUsername, configuredUsername);
            var validPassword = FixedTimeEquals(providedPassword, configuredPassword);

            if (!validUser || !validPassword)
            {
                logger.LogWarning(
                    "Failed login attempt for username={Username} from IP={RemoteIp}",
                    providedUsername, httpContext.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }

            var jwtSecret = config["Jwt:Secret"];
            if (string.IsNullOrWhiteSpace(jwtSecret))
            {
                logger.LogWarning("JWT secret missing while handling login");
                return Results.Unauthorized();
            }

            var secretBytes = Encoding.UTF8.GetBytes(jwtSecret);
            if (secretBytes.Length < 32)
            {
                logger.LogWarning("JWT secret too short while handling login");
                return Results.Unauthorized();
            }

            var now = DateTime.UtcNow;
            var expires = req.RememberMe ? now.AddDays(30) : now.AddHours(12);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, providedUsername),
                new Claim(JwtRegisteredClaimNames.UniqueName, providedUsername),
                new Claim(ClaimTypes.Name, providedUsername),
            };

            var token = new JwtSecurityToken(
                claims: claims,
                notBefore: now,
                expires: expires,
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(secretBytes),
                    SecurityAlgorithms.HmacSha256));

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            var cookieSecure = config.GetValue<bool?>("Auth:CookieSecure")
                ?? (!env.IsDevelopment() || httpContext.Request.IsHttps);

            httpContext.Response.Cookies.Append(CookieName, tokenString, new CookieOptions
            {
                HttpOnly = true,
                Secure = cookieSecure,
                SameSite = SameSiteMode.Strict,
                Expires = expires,
                Path = "/",
            });

            return Results.Ok(new LoginResponse(providedUsername, expires));
        })
        .RequireRateLimiting("login")
        .WithSummary("Authenticate user and issue JWT token");

        return app;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static bool FixedTimeEquals(string left, string right)
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