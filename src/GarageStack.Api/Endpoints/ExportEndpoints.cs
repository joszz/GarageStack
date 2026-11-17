using GarageStack.Api.Authentication;
using GarageStack.Api.Services;

namespace GarageStack.Api.Endpoints;

/// <summary>Everything GarageStack keeps, as one zip to download (see <see cref="DataExport"/>).</summary>
public static class ExportEndpoints
{
    internal const string Path = "/api/export";

    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        // The clock, not request input.
        var clock = app.ServiceProvider.GetRequiredService<TimeProvider>();

        app.MapGet(Path, (HttpContext httpContext, DataExport export, CancellationToken ct) =>
            ZipExport.DownloadAsync(
                file => export.WriteAsync(file, SessionPrincipal.ResolveAccountKey(httpContext.User), ct),
                $"garagestack-export-{clock.GetUtcNow():yyyy-MM-dd}.zip"))
        .WithTags("Export")
        .RequireAuthorization()
        .RequireRateLimiting("export")
        .Produces(StatusCodes.Status200OK, typeof(Stream), "application/zip")
        .WithSummary("Download everything GarageStack keeps, as a zip of JSON files and a telemetry CSV per car");

        return app;
    }
}
