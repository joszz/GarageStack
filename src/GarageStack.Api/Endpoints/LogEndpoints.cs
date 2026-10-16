using GarageStack.Core.Helpers;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// The newest log entries of the API and the Worker, for the log viewer. The API reads its own
/// files, and the Worker's from LogFiles:WorkerDirectory (WORKER_LOG_DIRECTORY): the Compose file
/// mounts the Worker's log volume there read-only, and the all-in-one image points it at /data/worker.
/// </summary>
public static class LogEndpoints
{
    private const string ApiSource = "api";
    private const string WorkerSource = "worker";
    private const int DefaultLimit = 200;
    private const int MaxLimit = 1000;

    // How much of each service's newest files a request reads: thousands of entries, a few MB.
    private const long MaxBytesPerSource = 8L * 1024 * 1024;

    public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app)
    {
        // Captured once: the log directories do not change while the API runs.
        var directories = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Where Serilog writes them: "logs" under the working directory (see AddGarageStackSerilog).
            [ApiSource] = Path.GetFullPath("logs"),
            [WorkerSource] = app.ServiceProvider.GetRequiredService<IConfiguration>()["LogFiles:WorkerDirectory"] is { Length: > 0 } worker
                ? worker
                : null,
        };

        app.MapGet("/api/logs", (string? source, LogLevelName? level, string? search, int? limit) =>
        {
            var wanted = source is null or "all" ? directories.Keys.ToArray() : [source];
            if (wanted.Any(s => !directories.ContainsKey(s)))
                return ApiProblems.BadRequest("logs.sourceUnknown", "source must be api, worker or all");

            var filter = new LogFilter(level ?? LogLevelName.Information, search?.Trim());
            var count = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var entries = wanted
                .Where(s => directories[s] is not null)
                .SelectMany(s => LogFileReader.ReadRecent(directories[s]!, s, filter, count, MaxBytesPerSource))
                .OrderByDescending(e => e.Time)
                .Take(count)
                .ToList();

            return Results.Ok(new LogPageDto(
                entries,
                [.. directories.Select(d => new LogSourceDto(d.Key, d.Value is not null && Directory.Exists(d.Value)))]));
        })
        .WithTags("Logs")
        .RequireAuthorization()
        .WithSummary("The newest log entries of the API and the Worker, newest first, from a level up and matching a search");

        return app;
    }
}

/// <summary>A log source and whether the API can read its files.</summary>
public record LogSourceDto(string Name, bool Available);

public record LogPageDto(IReadOnlyList<LogEntry> Entries, IReadOnlyList<LogSourceDto> Sources);
