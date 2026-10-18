using GarageStack.Core.Helpers;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// The log entries of the API and the Worker, a page at a time, for the log viewer: the newest
/// page, the one before an earlier page's <c>older</c> cursor as the list scrolls, and what was
/// written since its <c>newer</c> cursor in live mode. The API reads its own files, and the
/// Worker's from LogFiles:WorkerDirectory (WORKER_LOG_DIRECTORY): the Compose file mounts the
/// Worker's log volume there read-only, and the all-in-one image points it at /data/worker.
/// </summary>
public static class LogEndpoints
{
    internal const string Path = "/api/logs";

    private const string ApiSource = "api";
    private const string WorkerSource = "worker";
    private const int DefaultLimit = 200;
    private const int MaxLimit = 1000;

    // How much of each service's files a request reads at most: thousands of entries. A page cut
    // short by it carries on from where it stopped.
    private const long MaxBytesPerSource = 8L * 1024 * 1024;

    public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app)
    {
        // Captured once: the log directories do not change while the API runs.
        var directories = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Where Serilog writes them: "logs" under the working directory (see AddGarageStackSerilog).
            [ApiSource] = System.IO.Path.GetFullPath("logs"),
            [WorkerSource] = app.ServiceProvider.GetRequiredService<IConfiguration>()["LogFiles:WorkerDirectory"] is { Length: > 0 } worker
                ? worker
                : null,
        };

        app.MapGet(Path, (string? source, LogLevelName? level, string? search, int? limit, string? before, string? after) =>
        {
            var wanted = source is null or "all" ? directories.Keys.ToArray() : [source];
            if (wanted.Any(s => !directories.ContainsKey(s)))
                return ApiProblems.BadRequest("logs.sourceUnknown", "source must be api, worker or all");

            LogCursor? olderThan = null;
            LogCursor? newerThan = null;
            if ((before is not null && after is not null)
                || (before is not null && !LogCursor.TryParse(before, out olderThan))
                || (after is not null && !LogCursor.TryParse(after, out newerThan)))
                return ApiProblems.BadRequest("logs.cursorInvalid", "before or after must be a cursor from an earlier page, not both");

            var page = LogFileReader.ReadPage(
                wanted.Where(s => directories[s] is not null).ToDictionary(s => s, s => directories[s]!, StringComparer.Ordinal),
                new LogFilter(level ?? LogLevelName.Information, search?.Trim()),
                Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit),
                MaxBytesPerSource,
                olderThan,
                newerThan);

            return Results.Ok(new LogPageDto(
                page.Entries,
                [.. directories.Select(d => new LogSourceDto(d.Key, d.Value is not null && Directory.Exists(d.Value)))],
                page.Older?.ToString(),
                page.Newer?.ToString(),
                page.Reset));
        })
        .WithTags("Logs")
        .RequireAuthorization()
        .WithSummary("A page of the API's and the Worker's log entries, newest first, from a level up and matching a search: the newest, the one before an older cursor, or what came after a newer one");

        return app;
    }
}

/// <summary>A log source and whether the API can read its files.</summary>
public record LogSourceDto(string Name, bool Available);

/// <summary>
/// A page of entries, newest first. Older goes back as <c>before</c> for the page after it, null
/// once nothing older is left; Newer goes back as <c>after</c> for what is written since. Reset
/// says the entries replace the list rather than extend it: a first page, or a live read that
/// found more than a page.
/// </summary>
public record LogPageDto(
    IReadOnlyList<LogEntry> Entries, IReadOnlyList<LogSourceDto> Sources, string? Older, string? Newer, bool Reset);
