namespace GarageStack.Api.Services;

/// <summary>
/// Where the log files of the API and the Worker are, for the log viewer and the diagnostics. The
/// API reads its own files, and the Worker's from LogFiles:WorkerDirectory (WORKER_LOG_DIRECTORY):
/// the Compose file mounts the Worker's log volume there read-only, and the all-in-one image
/// points it at /data/worker. Fixed for the life of the process.
/// </summary>
public sealed class LogDirectories(IReadOnlyDictionary<string, string?> bySource)
{
    public const string ApiSource = "api";
    public const string WorkerSource = "worker";

    /// <summary>Each source with its directory, or null when it has none configured.</summary>
    public IReadOnlyDictionary<string, string?> BySource { get; } = bySource;

    /// <summary>The sources that have a directory, as <see cref="Core.Helpers.LogFileReader"/> takes them.</summary>
    public IReadOnlyDictionary<string, string> Configured { get; } = bySource
        .Where(d => d.Value is not null)
        .ToDictionary(d => d.Key, d => d.Value!, StringComparer.Ordinal);

    public static LogDirectories From(IConfiguration configuration) => new(new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        // Where Serilog writes them: "logs" under the working directory (see AddGarageStackSerilog).
        [ApiSource] = Path.GetFullPath("logs"),
        [WorkerSource] = configuration["LogFiles:WorkerDirectory"] is { Length: > 0 } worker ? worker : null,
    });
}
