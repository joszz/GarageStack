using GarageStack.Core.Models;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Api.Endpoints;

/// <summary>Browser push subscription management (Web Push / VAPID).</summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var push = app.MapGroup("/api/push")
            .WithTags("Push Notifications")
            .RequireAuthorization();

        push.MapGet("/vapid-public-key", (IConfiguration config) =>
        {
            var key = config["Vapid:PublicKey"];
            return string.IsNullOrWhiteSpace(key)
                ? ApiProblems.Problem(StatusCodes.Status503ServiceUnavailable, "push.notConfigured", "VAPID keys not configured")
                : Results.Ok(new { publicKey = key });
        })
        .WithSummary("Get VAPID public key for push subscription");

        push.MapPost("/subscribe", async (PushSubscribeRequest req, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Endpoint) ||
                string.IsNullOrWhiteSpace(req.P256DhKey) ||
                string.IsNullOrWhiteSpace(req.AuthKey))
                return ApiProblems.BadRequest("push.subscriptionIncomplete", "Endpoint, P256DhKey and AuthKey are required");

            await UpsertSubscriptionAsync(db, req, ct);

            return Results.Ok();
        })
        .WithSummary("Register a browser push subscription");

        push.MapPost("/unsubscribe", async (PushUnsubscribeRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == req.Endpoint, ct);
            if (sub is not null)
            {
                db.PushSubscriptions.Remove(sub);
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok();
        })
        .WithSummary("Remove a push subscription");

        return app;
    }

    /// <summary>
    /// Stores <paramref name="req"/> against its endpoint, inserting or updating as needed. The app
    /// re-registers its subscription on every load rather than only when the user opts in, so this
    /// runs often and has to be both idempotent and safe to run twice at once.
    /// </summary>
    internal static async Task UpsertSubscriptionAsync(
        AppDbContext db,
        PushSubscribeRequest req,
        CancellationToken ct)
    {
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == req.Endpoint, ct);

        if (existing is not null)
        {
            // Keys are rewritten only when they actually changed, so the common case (the same
            // subscription arriving again on the next load) costs one read and no write at all.
            if (existing.P256DhKey == req.P256DhKey && existing.AuthKey == req.AuthKey)
                return;

            existing.P256DhKey = req.P256DhKey;
            existing.AuthKey = req.AuthKey;
            await db.SaveChangesAsync(ct);
            return;
        }

        db.PushSubscriptions.Add(new PushSubscription
        {
            Endpoint = req.Endpoint,
            P256DhKey = req.P256DhKey,
            AuthKey = req.AuthKey,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two clients (or two tabs) can reach the insert together, and the unique index on
            // Endpoint rejects the second one. Re-read rather than assume that is what happened:
            // if the row is there now it was that race, and the keys match anyway since a browser
            // has exactly one key pair per endpoint. If it is not, the save failed for a real
            // reason and the caller should hear about it.
            var raced = await db.PushSubscriptions
                .AsNoTracking()
                .AnyAsync(s => s.Endpoint == req.Endpoint, ct);
            if (!raced) throw;
        }
    }
}

public record PushSubscribeRequest(string Endpoint, string P256DhKey, string AuthKey);
public record PushUnsubscribeRequest(string Endpoint);
