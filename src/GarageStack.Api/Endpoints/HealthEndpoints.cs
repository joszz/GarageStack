namespace GarageStack.Api.Endpoints;

public static class HealthEndpoints
{
    // Liveness only -- confirms the ASP.NET Core pipeline is up and serving requests, which is
    // what Docker HEALTHCHECK and compose's depends_on: condition: service_healthy need to stop
    // the frontend/worker starting before the API is actually listening. Deliberately does not
    // check database connectivity: DEMO_MODE has no database at all, and a deep check here would
    // make the container report unhealthy during a transient DB reconnect even though the API
    // itself is fine.
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithTags("Health")
            .ExcludeFromDescription();

        return app;
    }
}
