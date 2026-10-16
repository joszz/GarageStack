using GarageStack.Api.Authentication;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// The signed-in account's read-only API tokens. A token is shown once, when it is made; after
/// that only its name and first characters are. Tokens themselves cannot use these endpoints.
/// </summary>
public static class ApiTokenEndpoints
{
    internal const string Path = "/api/tokens";

    public static IEndpointRouteBuilder MapApiTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Path)
            .WithTags("API tokens")
            .RequireAuthorization();

        group.MapGet("/", async (HttpContext httpContext, IApiTokenRepository tokens, CancellationToken ct) =>
        {
            if (SessionPrincipal.ResolveAccountKey(httpContext.User) is not { } account) return NoAccount();
            var list = await tokens.ListAsync(account, ct);
            return Results.Ok(list.Select(ToDto));
        })
        .WithSummary("List the signed-in account's API tokens, newest first, without the tokens themselves");

        group.MapPost("/", async (
            HttpContext httpContext, CreateApiTokenRequest req, IApiTokenRepository tokens, CancellationToken ct) =>
        {
            if (SessionPrincipal.ResolveAccountKey(httpContext.User) is not { } account) return NoAccount();

            var name = req.Name?.Trim() ?? "";
            if (name.Length == 0)
                return ApiProblems.BadRequest("token.nameRequired", "Give the token a name, to know it by later");
            if (name.Length > ApiTokenLimits.NameMaxLength)
                return ApiProblems.BadRequest("token.nameTooLong", $"Name must be {ApiTokenLimits.NameMaxLength} characters or fewer");
            if (await tokens.CountAsync(account, ct) >= ApiTokenLimits.MaxPerAccount)
                return ApiProblems.BadRequest("token.tooMany", $"An account can have {ApiTokenLimits.MaxPerAccount} tokens; revoke one first");

            var (token, hash, prefix) = ApiTokenAuthentication.Create();
            var saved = await tokens.AddAsync(new ApiToken { AccountKey = account, Name = name, TokenHash = hash, Prefix = prefix }, ct);
            return Results.Ok(new CreatedApiTokenDto(ToDto(saved), token));
        })
        .WithSummary("Make a read-only API token; the response is the only time the token is shown");

        group.MapDelete("/{id:int}", async (HttpContext httpContext, int id, IApiTokenRepository tokens, CancellationToken ct) =>
        {
            if (SessionPrincipal.ResolveAccountKey(httpContext.User) is not { } account) return NoAccount();
            return await tokens.DeleteAsync(account, id, ct) ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Revoke an API token: it stops working at once");

        return app;
    }

    private static ApiTokenDto ToDto(ApiToken t) => new(t.Id, t.Name, t.Prefix, t.CreatedAt, t.LastUsedAt);

    private static IResult NoAccount() =>
        ApiProblems.Problem(StatusCodes.Status403Forbidden, "token.noAccount",
            "This session names no account to make tokens for");
}

public record CreateApiTokenRequest(string? Name);

public record ApiTokenDto(int Id, string Name, string Prefix, DateTime CreatedAt, DateTime? LastUsedAt);

/// <summary>A token just made: <paramref name="Token"/> is shown this once and never again.</summary>
public record CreatedApiTokenDto(ApiTokenDto Details, string Token);
