using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using GarageStack.Core.Models;
using GarageStack.Data;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace GarageStack.Api.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshCookieName = "gs_refresh";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Auth")
            .RequireRateLimiting("fixed");

        group.MapPost("/login", async (
            LoginRequest req,
            AppDbContext db,
            IConfiguration config,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { error = "Email and password are required" });

            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == req.Email.ToLowerInvariant(), ct);

            if (user is null)
            {
                // Auto-register on first login (single-household self-hosted setup)
                user = new AppUser
                {
                    Email = req.Email.ToLowerInvariant(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                };
                db.AppUsers.Add(user);
                await db.SaveChangesAsync(ct);
            }
            else if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            {
                return Results.Unauthorized();
            }

            var accessToken = GenerateAccessToken(user, config);
            var refreshToken = await IssueRefreshToken(user, db, req.RememberMe, ct);

            SetRefreshCookie(ctx, refreshToken, req.RememberMe, config);

            return Results.Ok(new { accessToken, userId = user.Id, email = user.Email });
        })
        .WithSummary("Login with SAIC credentials")
        .AllowAnonymous();

        group.MapPost("/refresh", async (
            AppDbContext db,
            IConfiguration config,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var token = ctx.Request.Cookies[RefreshCookieName];
            if (string.IsNullOrWhiteSpace(token))
                return Results.Unauthorized();

            var stored = await db.UserRefreshTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Token == token && !t.Revoked, ct);

            if (stored is null || stored.ExpiresAt < DateTime.UtcNow)
                return Results.Unauthorized();

            // Rotate: revoke old, issue new
            stored.Revoked = true;
            var isRememberMe = (stored.ExpiresAt - DateTime.UtcNow).TotalDays > 8;
            var newRefresh = await IssueRefreshToken(stored.User, db, isRememberMe, ct);
            await db.SaveChangesAsync(ct);

            SetRefreshCookie(ctx, newRefresh, isRememberMe, config);

            var accessToken = GenerateAccessToken(stored.User, config);
            return Results.Ok(new { accessToken, userId = stored.User.Id, email = stored.User.Email });
        })
        .WithSummary("Refresh access token using httpOnly cookie")
        .AllowAnonymous();

        group.MapPost("/logout", async (
            AppDbContext db,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var token = ctx.Request.Cookies[RefreshCookieName];
            if (!string.IsNullOrWhiteSpace(token))
            {
                var stored = await db.UserRefreshTokens.FirstOrDefaultAsync(t => t.Token == token, ct);
                if (stored is not null)
                {
                    stored.Revoked = true;
                    await db.SaveChangesAsync(ct);
                }
            }

            ctx.Response.Cookies.Delete(RefreshCookieName);
            return Results.Ok();
        })
        .WithSummary("Logout and revoke refresh token");

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = user.FindFirst(ClaimTypes.Email)?.Value;
            return Results.Ok(new { userId = int.Parse(id!), email });
        })
        .WithSummary("Get current authenticated user")
        .RequireAuthorization();

        group.MapGet("/debug/vehicles", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var email = user.FindFirst(ClaimTypes.Email)?.Value;
            var all = await db.Vehicles.AsNoTracking().ToListAsync(ct);
            return Results.Ok(new
            {
                jwtEmail = email,
                totalVehicles = all.Count,
                vehicles = all.Select(v => new { v.Id, v.Vin, v.SaicUser, saicUserLower = v.SaicUser?.ToLower(), matched = v.SaicUser?.ToLower() == email }),
            });
        })
        .WithSummary("Debug: show all vehicles and match against JWT email")
        .RequireAuthorization();

        // Cleanup endpoint: remove old revoked/expired tokens (maintenance)
        group.MapDelete("/tokens/expired", async (AppDbContext db, CancellationToken ct) =>
        {
            var cutoff = DateTime.UtcNow;
            var old = await db.UserRefreshTokens
                .Where(t => t.Revoked || t.ExpiresAt < cutoff)
                .ToListAsync(ct);
            db.UserRefreshTokens.RemoveRange(old);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { removed = old.Count });
        })
        .WithSummary("Remove expired/revoked refresh tokens")
        .RequireAuthorization();

        return app;
    }

    private static string GenerateAccessToken(AppUser user, IConfiguration config)
    {
        var secret = config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret not configured");
        var issuer = config["Jwt:Issuer"] ?? "GarageStack";
        var audience = config["Jwt:Audience"] ?? "GarageStack";
        var minutesStr = config["Jwt:AccessTokenMinutes"];
        var minutes = int.TryParse(minutesStr, out var m) ? m : 15;

        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<string> IssueRefreshToken(AppUser user, AppDbContext db, bool rememberMe, CancellationToken ct)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var days = rememberMe ? 30 : 7;

        db.UserRefreshTokens.Add(new UserRefreshToken
        {
            UserId = user.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(days),
        });
        await db.SaveChangesAsync(ct);
        return token;
    }

    private static void SetRefreshCookie(HttpContext ctx, string token, bool rememberMe, IConfiguration config)
    {
        var days = rememberMe ? 30 : 7;
        ctx.Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !ctx.Request.IsHttps ? false : true,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromDays(days),
            Path = "/api/auth",
        });
    }
}

public record LoginRequest(string Email, string Password, bool RememberMe = false);
