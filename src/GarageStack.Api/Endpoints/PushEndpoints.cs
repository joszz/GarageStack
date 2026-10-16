using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Endpoints;

/// <summary>Browser push subscription management (Web Push / VAPID).</summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var vapid = app.ServiceProvider.GetRequiredService<VapidOptions>();

        var push = app.MapGroup("/api/push")
            .WithTags("Push Notifications")
            .RequireAuthorization();

        push.MapGet("/vapid-public-key", () =>
            string.IsNullOrWhiteSpace(vapid.PublicKey)
                ? ApiProblems.Problem(StatusCodes.Status503ServiceUnavailable, "push.notConfigured", "VAPID keys not configured")
                : Results.Ok(new { publicKey = vapid.PublicKey }))
        .WithSummary("Get VAPID public key for push subscription");

        push.MapPost("/subscribe", async (PushSubscribeRequest req, IPushSubscriptionRepository subscriptions, CancellationToken ct) =>
        {
            if (ValidateSubscription(req) is { } error)
                return ApiProblems.BadRequest(error);

            await UpsertSubscriptionAsync(subscriptions, req, ct);

            return Results.Ok();
        })
        .WithSummary("Register a browser push subscription");

        push.MapPost("/unsubscribe", async (PushUnsubscribeRequest req, IPushSubscriptionRepository subscriptions, CancellationToken ct) =>
        {
            await subscriptions.RemoveAsync([req.Endpoint], ct);
            return Results.Ok();
        })
        .WithSummary("Remove a push subscription");

        return app;
    }

    // Far longer than any push service's endpoint or key, short enough for the unique index.
    private const int MaxEndpointLength = 2048;
    private const int MaxKeyLength = 256;

    /// <summary>
    /// Whether <paramref name="req"/> looks like a subscription a browser handed out. The Worker
    /// POSTs every notification to the endpoint, so it must be a public https address: a push
    /// service, never a host on the server's own network.
    /// </summary>
    internal static ValidationError? ValidateSubscription(PushSubscribeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Endpoint) ||
            string.IsNullOrWhiteSpace(req.P256DhKey) ||
            string.IsNullOrWhiteSpace(req.AuthKey))
            return new("push.subscriptionIncomplete", "Endpoint, P256DhKey and AuthKey are required");

        if (req.Endpoint.Length > MaxEndpointLength || req.P256DhKey.Length > MaxKeyLength || req.AuthKey.Length > MaxKeyLength
            || !IsPublicHttpsUrl(req.Endpoint))
            return new("push.subscriptionInvalid", "Endpoint must be a push service's https address");

        return null;
    }

    private static bool IsPublicHttpsUrl(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || uri.HostNameType != UriHostNameType.Dns)
            return false;

        // A push service has a public DNS name: dotted, and not one of the names that only resolve
        // inside a network.
        var host = uri.IdnHost.TrimEnd('.');
        return host.Contains('.')
            && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Stores <paramref name="req"/> against its endpoint. A request without the type list comes
    /// from a page that predates it: it says nothing about which types the device wants, so
    /// whatever is stored stays.
    /// </summary>
    internal static Task UpsertSubscriptionAsync(
        IPushSubscriptionRepository subscriptions, PushSubscribeRequest req, CancellationToken ct) =>
        subscriptions.UpsertAsync(
            req.Endpoint, req.P256DhKey, req.AuthKey,
            req.ExcludedCategories is { } requested ? KnownGroups(requested) : null,
            ct);

    // Only the types the checklist offers, once each and in one order, so an unchanged list
    // compares equal to the stored one whatever order the page sent it in.
    private static List<string> KnownGroups(IEnumerable<string> requested) =>
        [.. requested.Where(NotificationCategories.Groups.Contains).Distinct().Order(StringComparer.Ordinal)];
}

// ExcludedCategories: the notification types this device switched off. Null leaves the stored list as it is.
public record PushSubscribeRequest(string Endpoint, string P256DhKey, string AuthKey, IReadOnlyList<string>? ExcludedCategories = null);
public record PushUnsubscribeRequest(string Endpoint);
