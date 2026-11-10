using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Caching.Memory;

namespace GarageStack.Api.Endpoints;

public static class DawarichEndpoints
{
    public static IEndpointRouteBuilder MapDawarichEndpoints(this IEndpointRouteBuilder app)
    {
        var options = app.ServiceProvider.GetRequiredService<DawarichOptions>();
        var time = app.ServiceProvider.GetRequiredService<TimeProvider>();
        app.MapGet("/api/dawarich/status", async (IDawarichClient client, CancellationToken ct) =>
            TypedResults.Ok(new DawarichStatusResponse(options.Enabled,
                options.Enabled ? Health(await client.CheckAsync(ct)) : "disabled")))
            .RequireAuthorization().RequireRateLimiting("dawarich").WithTags("Dawarich")
            .WithSummary("Check whether Dawarich is configured and reachable with the configured key");

        var group = app.MapGroup("/api/vehicles/{vin}/phone-trackers")
            .WithTags("Dawarich").RequireAuthorization().RequireRateLimiting("dawarich")
            .AddEndpointFilter<ResolveVehicleFilter>();

        group.MapGet("", async (HttpContext http, IPhoneFusionRepository repo, IDawarichClient client,
            IMemoryCache cache, ILoggerFactory logs, CancellationToken ct) =>
        {
            if (!options.Enabled) return Disabled();
            var linked = await repo.ListAsync(http.ResolvedVehicle().Id, ct);
            try
            {
                var trackers = await cache.GetOrCreateAsync("dawarich-trackers", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                    return await client.GetTrackersAsync(time.GetUtcNow().UtcDateTime.AddDays(-30), ct);
                }) ?? [];
                return Results.Ok(new PhoneTrackersResponse(linked, trackers, null));
            }
            catch (DawarichException ex)
            {
                logs.CreateLogger("Dawarich").LogWarning("Dawarich tracker discovery failed: {Reason}", ex.Message);
                // Linked identifiers remain editable even when upstream discovery is unavailable.
                return Results.Ok(new PhoneTrackersResponse(linked, [], ex.BadKey ? "badKey" : "unreachable"));
            }
        }).WithSummary("List linked phone trackers and trackers seen in Dawarich during the last 30 days");

        group.MapPut("", async (HttpContext http, SavePhoneTrackersRequest request,
            IPhoneFusionRepository repo, CancellationToken ct) =>
        {
            if (!options.Enabled) return Disabled();
            if (Validate(request) is { } problem) return ApiProblems.BadRequest(problem);
            var id = http.ResolvedVehicle().Id;
            await repo.SetAsync(id, request.TrackerIds!, time.GetUtcNow().UtcDateTime, ct);
            return Results.Ok(await repo.ListAsync(id, ct));
        }).WithSummary("Replace the vehicle's linked phone trackers; an empty set turns phone fusion off");

        group.MapPost("/backfill", async (HttpContext http, PhoneBackfillRequestDto request,
            IPhoneFusionRepository repo, CancellationToken ct) =>
        {
            if (!options.Enabled) return Disabled();
            var now = time.GetUtcNow();
            if (request.From is not { } from || from.Year < 2000 || from > now)
                return ApiProblems.BadRequest("dawarich.invalidFrom", "Choose a from date between 2000 and now.");
            var id = http.ResolvedVehicle().Id;
            if ((await repo.ListAsync(id, ct)).Count == 0)
                return ApiProblems.BadRequest("dawarich.notLinked", "Link a phone tracker before requesting backfill.");
            await repo.RequestBackfillAsync(id, from.UtcDateTime, now.UtcDateTime, ct);
            return Results.Accepted();
        }).WithSummary("Queue older unedited trips for phone fusion, from the supplied date until now");
        return app;
    }

    internal static ValidationError? Validate(SavePhoneTrackersRequest request) =>
        request.TrackerIds is null || request.TrackerIds.Count > VehiclePhoneTracker.MaxTrackers ||
        request.TrackerIds.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > VehiclePhoneTracker.MaxTrackerLength ||
            t != t.Trim() || t.Any(char.IsControl)) ||
        request.TrackerIds.Distinct(StringComparer.Ordinal).Count() != request.TrackerIds.Count
            ? new("dawarich.invalidTrackers", "Supply at most 10 distinct nonblank tracker identifiers, each at most 200 characters.")
            : null;

    private static string Health(DawarichHealth health) => health switch
    {
        DawarichHealth.Ok => "ok",
        DawarichHealth.BadKey => "badKey",
        _ => "unreachable",
    };

    private static IResult Disabled() =>
        ApiProblems.Problem(404, "dawarich.disabled", "Dawarich is not configured on this deployment.");
}

/// <summary>Deployment configuration and a credential check, never the URL or key.</summary>
public sealed record DawarichStatusResponse(bool Configured, string Status);

/// <summary>Linked tracker identifiers and available trackers, with an explicit discovery error when unavailable.</summary>
public sealed record PhoneTrackersResponse(
    IReadOnlyList<LinkedPhoneTracker> Linked, IReadOnlyList<PhoneTracker> Available, string? Error);

/// <summary>The full set of trackers linked to one vehicle.</summary>
public sealed record SavePhoneTrackersRequest(IReadOnlyList<string>? TrackerIds);

/// <summary>The earliest date to consider for an explicit historical backfill.</summary>
public sealed record PhoneBackfillRequestDto(DateTimeOffset? From);
