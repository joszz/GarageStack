using GarageStack.Api.Services;

namespace GarageStack.Api.Endpoints;

/// <summary>An anonymized zip to attach to a bug report (see <see cref="DiagnosticsExport"/>).</summary>
public static class DiagnosticsEndpoints
{
    internal const string Path = "/api/diagnostics";

    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        // The clock, not request input.
        var clock = app.ServiceProvider.GetRequiredService<TimeProvider>();

        app.MapGet(Path, (DiagnosticsExport diagnostics, CancellationToken ct) =>
            ZipExport.DownloadAsync(
                file => diagnostics.WriteAsync(file, ct),
                $"garagestack-diagnostics-{clock.GetUtcNow():yyyy-MM-dd}.zip"))
        .WithTags("Diagnostics")
        .RequireAuthorization()
        .RequireRateLimiting("export")
        .Produces(StatusCodes.Status200OK, typeof(Stream), "application/zip")
        .WithSummary("Download anonymized diagnostics for a bug report: version, health, car details, a week of readings without positions, and a week of warnings from the logs");

        return app;
    }
}
