using GarageStack.Api.Services;

namespace GarageStack.Api.Endpoints;

/// <summary>Whether the SAIC gateway is signed in to the MG account.</summary>
/// <param name="SignedIn">
/// True while it is; false after a failed sign-in, until the next good one (it tries again every
/// fifteen minutes); null while the gateway has not said, or the Worker is older than this answer.
/// </param>
public sealed record GatewayStatus(bool? SignedIn);

/// <summary>The gateway's sign-in to the MG account, so the app can tell it apart from the car being offline.</summary>
public static class GatewayEndpoints
{
    public static IEndpointRouteBuilder MapGatewayEndpoints(this IEndpointRouteBuilder app)
    {
        var monitor = app.ServiceProvider.GetRequiredService<GatewaySignInMonitor>();

        // Changes arrive over SignalR (gatewayStatus); this is the answer a page starts from.
        app.MapGet("/api/gateway", () => Results.Ok(monitor.Status))
            .WithTags("Gateway")
            .Produces<GatewayStatus>()
            .WithSummary("Whether the gateway is signed in to the MG account")
            .RequireAuthorization();

        return app;
    }
}
