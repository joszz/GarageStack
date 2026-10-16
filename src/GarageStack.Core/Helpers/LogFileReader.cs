using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Reads the newest entries back out of the rolling log files both services write
/// (logs/api-20261016.log, and logs/api-20261016_001.log once a day's file passed its size cap). An
/// entry starts "2026-10-16 06:30:38.268 +02:00 [WRN] message"; an exception's lines follow it.
/// Only the end of the newest files is read, so a long history costs nothing to look at.
/// </summary>
public static partial class LogFileReader
{
    private static readonly Dictionary<string, LogLevelName> Levels = new(StringComparer.Ordinal)
    {
        ["VRB"] = LogLevelName.Verbose,
        ["DBG"] = LogLevelName.Debug,
        ["INF"] = LogLevelName.Information,
        ["WRN"] = LogLevelName.Warning,
        ["ERR"] = LogLevelName.Error,
        ["FTL"] = LogLevelName.Fatal,
    };

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[([A-Z]{3})\] (.*)$")]
    private static partial Regex EntryStart();

    /// <summary>
    /// The newest entries of <paramref name="source"/>'s files in <paramref name="directory"/> that
    /// <paramref name="filter"/> lets through, newest first, at most <paramref name="limit"/>, from
    /// at most the last <paramref name="maxBytes"/> of its files.
    /// </summary>
    public static IReadOnlyList<LogEntry> ReadRecent(
        string directory, string source, LogFilter filter, int limit, long maxBytes)
    {
        if (!Directory.Exists(directory)) return [];

        // Newest first: the date sorts by day, and a day's _001, _002 files after its first one.
        var files = Directory.GetFiles(directory, $"{source}-*.log")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal);
        var found = new List<LogEntry>();
        var budget = maxBytes;

        foreach (var file in files)
        {
            if (budget <= 0 || found.Count >= limit) break;
            var (text, partial) = ReadTail(file, budget, out var read);
            budget -= read;

            var entries = Parse(text, source, skipFirstLine: partial);
            for (var i = entries.Count - 1; i >= 0 && found.Count < limit; i--)
                if (filter.Allows(entries[i])) found.Add(entries[i]);
        }

        return found;
    }

    /// <summary>Entries in the order they were written. A line that starts no entry belongs to the one before it.</summary>
    public static List<LogEntry> Parse(string text, string source, bool skipFirstLine = false)
    {
        var entries = new List<LogEntry>();
        LogEntry? current = null;
        StringBuilder? more = null;

        void Finish()
        {
            if (current is null) return;
            entries.Add(more is { Length: > 0 } ? current with { Exception = more.ToString().TrimEnd() } : current);
            current = null;
            more = null;
        }

        var lines = text.Split('\n');
        for (var i = skipFirstLine ? 1 : 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var match = EntryStart().Match(line);
            if (match.Success && Levels.TryGetValue(match.Groups[2].Value, out var level))
            {
                Finish();
                var time = DateTimeOffset.ParseExact(match.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
                current = new LogEntry(time, level, source, match.Groups[3].Value, null);
            }
            else if (current is not null && line.Length > 0)
            {
                (more ??= new StringBuilder()).AppendLine(line);
            }
        }

        Finish();
        return entries;
    }

    // The last `budget` bytes of the file, and whether that starts mid-line. The service keeps
    // writing to its newest file, so it is opened sharing write access.
    private static (string Text, bool Partial) ReadTail(string path, long budget, out long read)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - budget);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        read = stream.Length - start;
        return (text, start > 0);
    }
}

// By name over the wire, as Serilog names them: the log viewer shows and filters by these.
[JsonConverter(typeof(JsonStringEnumConverter<LogLevelName>))]
public enum LogLevelName
{
    Verbose,
    Debug,
    Information,
    Warning,
    Error,
    Fatal,
}

/// <summary>One log entry: when, how bad, which service, what it said, and an exception's lines if any.</summary>
public sealed record LogEntry(DateTimeOffset Time, LogLevelName Level, string Source, string Message, string? Exception);

/// <summary>Which entries to show: from <paramref name="MinimumLevel"/> up, and containing <paramref name="Search"/> if given.</summary>
public sealed record LogFilter(LogLevelName MinimumLevel, string? Search)
{
    public bool Allows(LogEntry entry) =>
        entry.Level >= MinimumLevel
        && (string.IsNullOrWhiteSpace(Search)
            || entry.Message.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || entry.Exception?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true);
}
