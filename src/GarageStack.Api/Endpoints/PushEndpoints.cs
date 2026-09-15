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
                ? Results.Problem("VAPID keys not configured")
                : Results.Ok(new { publicKey = key });
        })
        .WithSummary("Get VAPID public key for push subscription");

        push.MapPost("/subscribe", async (PushSubscribeRequest req, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Endpoint) ||
                string.IsNullOrWhiteSpace(req.P256DhKey) ||
                string.IsNullOrWhiteSpace(req.AuthKey))
                return Results.BadRequest(new { error = "Endpoint, P256DhKey and AuthKey are required" });

            var existing = await db.PushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == req.Endpoint, ct);

            if (existing is null)
            {
                db.PushSubscriptions.Add(new PushSubscription
                {
                    Endpoint = req.Endpoint,
                    P256DhKey = req.P256DhKey,
                    AuthKey = req.AuthKey,
                });
            }
            else if (existing.P256DhKey != req.P256DhKey || existing.AuthKey != req.AuthKey)
            {
                existing.P256DhKey = req.P256DhKey;
                existing.AuthKey = req.AuthKey;
            }

            await db.SaveChangesAsync(ct);

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
}

public record PushSubscribeRequest(string Endpoint, string P256DhKey, string AuthKey);
public record PushUnsubscribeRequest(string Endpoint);
