using GarageStack.Api.Authentication;
using GarageStack.Api.Services;

namespace GarageStack.Api.Endpoints;

/// <summary>Everything GarageStack keeps, as one zip to download (see <see cref="DataExport"/>).</summary>
public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/export", async (HttpContext httpContext, DataExport export, CancellationToken ct) =>
        {
            // Built in a temporary file rather than in memory or straight into the response: the
            // telemetry alone can run to hundreds of megabytes, and the zip writer writes
            // synchronously, which the response does not take. The file goes when the response is sent.
            var file = new FileStream(
                Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                await export.WriteAsync(file, SessionPrincipal.ResolveAccountKey(httpContext.User), ct);
                file.Position = 0;
                return Results.File(file, "application/zip", $"garagestack-export-{DateTime.UtcNow:yyyy-MM-dd}.zip");
            }
            catch
            {
                await file.DisposeAsync();
                throw;
            }
        })
        .WithTags("Export")
        .RequireAuthorization()
        .RequireRateLimiting("export")
        .WithSummary("Download everything GarageStack keeps, as a zip of JSON files and a telemetry CSV per car");

        return app;
    }
}
