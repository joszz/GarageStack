using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace GarageStack.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/login", (LoginRequest req, IConfiguration config, ILoggerFactory loggerFactory) =>
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
                return Results.Unauthorized();

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
            var expires = now.AddHours(12);

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
            return Results.Ok(new LoginResponse(tokenString, providedUsername, expires));
        })
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

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string Token, string Username, DateTime ExpiresAtUtc);