using System.Text.Json;
using GarageStack.Api.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GarageStack.Api.Endpoints;

public static class HealthEndpoints
{
    /// <summary>The readiness path, under /api/ so it is reachable through the web interface's nginx.</summary>
    public const string ReadinessPath = "/api/health";

    // How long one dependency may take to answer before it counts as down.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The checks behind <see cref="ReadinessPath"/>. Demo mode has no broker and no Worker, so it
    /// only checks its (in-memory) database.
    /// </summary>
    public static IServiceCollection AddGarageStackHealthChecks(this IServiceCollection services, bool isDemoMode)
    {
        var checks = services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", timeout: CheckTimeout);

        if (!isDemoMode)
        {
            services.AddSingleton(sp => new WorkerHeartbeatMonitor(TimeProvider.System));
            checks.AddCheck<MqttHealthCheck>("mqtt", timeout: CheckTimeout)
                .AddCheck<WorkerHealthCheck>("worker", timeout: CheckTimeout);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness only -- confirms the ASP.NET Core pipeline is up and serving requests, which is
        // what Docker HEALTHCHECK and compose's depends_on: condition: service_healthy need to stop
        // the frontend/worker starting before the API is actually listening. Deliberately does not
        // check database connectivity: DEMO_MODE has no database at all, and a deep check here would
        // make the container report unhealthy during a transient DB reconnect even though the API
        // itself is fine. ReadinessPath is the deep check, for monitors.
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithTags("Health")
            .ExcludeFromDescription();

        // Readiness for an uptime monitor: the database, the broker and the Worker, each by name.
        // Answers 503 when one is down. No sign-in, and no detail beyond each check's state.
        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions { ResponseWriter = WriteReportAsync });

        return app;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static Task WriteReportAsync(HttpContext context, HealthReport report)
    {
        var body = new
        {
            status = Name(report.Status),
            checks = report.Entries.ToDictionary(e => e.Key, e => Name(e.Value.Status)),
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));

        static string Name(HealthStatus status) => status.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Whether <paramref name="path"/> is one of the health paths, which monitors and Docker probe
    /// every few seconds: logging each answer would fill the log files with nothing.
    /// </summary>
    public static bool IsHealthProbe(PathString path) =>
        path.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || path.Equals(ReadinessPath, StringComparison.OrdinalIgnoreCase);
}
