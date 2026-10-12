using GarageStack.Api.Services;

namespace GarageStack.Api.Endpoints;

/// <summary>Whether a newer GarageStack release is out, for the footer.</summary>
public static class UpdateEndpoints
{
    public static IEndpointRouteBuilder MapUpdateEndpoints(this IEndpointRouteBuilder app)
    {
        var updates = app.ServiceProvider.GetRequiredService<UpdateCheckService>();

        // Signed in only: an anonymous visitor has no use for the version this install runs.
        app.MapGet("/api/updates", () => Results.Ok(updates.Latest))
            .WithTags("Updates")
            .WithSummary("Whether a newer GarageStack release is available")
            .RequireAuthorization();

        return app;
    }
}
