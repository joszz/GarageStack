using GarageStack.Api.Authentication;
using GarageStack.Api.Reference;
using GarageStack.Api.Services;
using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;
using Microsoft.OpenApi;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// A calendar feed (iCalendar) of the APK expiry date, maintenance due dates and the coming climate
/// schedule runs, for a calendar app to subscribe to. Calendar apps send no header, so the feed is
/// opened by a token in its address: a token that opens the feed and nothing else
/// (<see cref="ApiTokenScope.Calendar"/>), one per account, made and revoked from a signed-in
/// session.
/// </summary>
public static class CalendarEndpoints
{
    internal const string Path = "/api/calendar";
    internal const string LinkPath = Path + "/link";
    internal const string FeedPath = Path + "/feed.ics";

    /// <summary>The query parameter the feed's token travels in.</summary>
    internal const string TokenParameter = "token";

    /// <summary>The rate limit policy the feed is held to.</summary>
    internal const string RateLimitPolicy = "calendar";

    private const string LinkName = "Calendar feed";

    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        // Deployment configuration and the clock, fixed for the process lifetime.
        var clock = app.ServiceProvider.GetRequiredService<TimeProvider>();
        var rdwEnabled = app.ServiceProvider.GetRequiredService<RdwOptions>().Enabled;

        app.MapGet(FeedPath, async (
                HttpContext httpContext,
                IApiTokenRepository tokens,
                IVehicleRepository vehicles,
                IRdwRepository rdw,
                IMaintenanceRepository maintenance,
                IClimateScheduleRepository schedules,
                IStringLocalizer<CalendarStrings> strings,
                CancellationToken ct) =>
            {
                var presented = httpContext.Request.Query[TokenParameter].ToString();
                var token = presented.Length == 0
                    ? null
                    : await tokens.FindByHashAsync(ApiTokenAuthentication.Hash(presented), ct);
                if (token is not { Scope: ApiTokenScope.Calendar })
                    return ApiProblems.Problem(StatusCodes.Status401Unauthorized, "calendar.unknownLink",
                        "This calendar link is unknown, or was replaced or stopped; make a new one in Settings");

                var now = clock.GetUtcNow().UtcDateTime;
                if (ApiTokenAuthentication.IsUseWorthNoting(token, now))
                    await tokens.MarkUsedAsync(token.Id, now, ct);

                var rdwByVehicle = rdwEnabled
                    ? (await rdw.ListAsync(ct)).ToDictionary(r => r.VehicleId)
                    : [];
                var itemsByVehicle = (await maintenance.ListAllAsync(ct)).ToLookup(m => m.VehicleId);
                var cars = new List<CalendarFeed.Car>();
                foreach (var vehicle in await vehicles.GetAllAsync(ct))
                {
                    cars.Add(new CalendarFeed.Car(
                        vehicle.Id,
                        rdwByVehicle.GetValueOrDefault(vehicle.Id),
                        [.. itemsByVehicle[vehicle.Id]],
                        await schedules.ListAsync(vehicle.Id, ct)));
                }

                // The address is the secret: no cache along the way keeps a copy.
                httpContext.Response.Headers.CacheControl = "no-store";
                httpContext.Response.Headers.ContentDisposition = "inline; filename=\"garagestack.ics\"";
                return Results.Text(CalendarFeed.Build(cars, now, strings), "text/calendar; charset=utf-8");
            })
            .WithTags("Calendar")
            .RequireRateLimiting(RateLimitPolicy)
            .Produces<string>(StatusCodes.Status200OK, "text/calendar")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithSummary("The calendar feed: APK expiry, maintenance due dates and the next two weeks of climate schedules, as iCalendar")
            .AddOpenApiOperationTransformer((operation, context, _) =>
            {
                operation.Security =
                [
                    new() { [new OpenApiSecuritySchemeReference(OpenApiSetup.CalendarTokenScheme, context.Document)] = [] },
                ];
                return Task.CompletedTask;
            });

        var link = app.MapGroup(LinkPath)
            .WithTags("Calendar")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        link.MapGet("/", async (HttpContext httpContext, IApiTokenRepository tokens, CancellationToken ct) =>
        {
            if (SessionPrincipal.ResolveAccountKey(httpContext.User) is not { } account) return NoAccount();
            var current = (await tokens.ListAsync(account, ApiTokenScope.Calendar, ct)).FirstOrDefault();
            return Results.Ok(new CalendarLinkDto(current is not null, current?.CreatedAt, current?.LastUsedAt));
        })
        .Produces<CalendarLinkDto>()
        .WithSummary("Whether the signed-in account has a calendar link, without the link itself");

        link.MapPost("/", async (HttpContext httpContext, IApiTokenRepository tokens, CancellationToken ct) =>
        {
            if (SessionPrincipal.ResolveAccountKey(httpContext.User) is not { } account) return NoAccount();

            var (token, hash, prefix) = ApiTokenAuthentication.Create();
            var saved = await tokens.ReplaceAsync(new ApiToken
            {
                AccountKey = account,
                Name = LinkName,
                TokenHash = hash,
                Prefix = prefix,
                Scope = ApiTokenScope.Calendar,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            }, ct);
            return Results.Ok(new CreatedCalendarLinkDto(new CalendarLinkDto(true, saved.CreatedAt, null), token));
        })
        .Produces<CreatedCalendarLinkDto>()
        .WithSummary("Make the account's calendar link, replacing the one it had; the response is the only time its token is shown");

        link.MapDelete("/", async (HttpContext httpContext, IApiTokenRepository tokens, CancellationToken ct) =>
        {
            if (SessionPrincipal.ResolveAccountKey(httpContext.User) is not { } account) return NoAccount();
            return await tokens.DeleteScopeAsync(account, ApiTokenScope.Calendar, ct) ? Results.NoContent() : Results.NotFound();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Stop the account's calendar link: calendar apps get nothing more from it");

        return app;
    }

    private static IResult NoAccount() =>
        ApiProblems.Problem(StatusCodes.Status403Forbidden, "calendar.noAccount",
            "This session names no account to make a calendar link for");
}

/// <summary>The account's calendar link, as Settings shows it.</summary>
/// <param name="Exists">Whether the account has one.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="LastUsedAt">When a calendar app last fetched it, to the minute.</param>
public record CalendarLinkDto(bool Exists, DateTime? CreatedAt, DateTime? LastUsedAt);

/// <summary>A calendar link just made.</summary>
/// <param name="Details">What Settings shows of it from now on.</param>
/// <param name="Token">The token for the feed's address, shown this once and never again.</param>
public record CreatedCalendarLinkDto(CalendarLinkDto Details, string Token);
