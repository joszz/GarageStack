using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Reads entries back out of the rolling log files both services write (logs/api-20261016.log,
/// and logs/api-20261016_001.log once a day's file passed its size cap), a page at a time, newest
/// first. An entry starts "2026-10-16 06:30:38.268 +02:00 [WRN] message"; an exception's lines
/// follow it. A page reads backwards from the newest end, or from where the page before it
/// stopped, only as far as it takes to fill the page, so scrolling back through a long history
/// costs one page's reading at a time; a live read stops where the one before it began. An entry
/// starts only at a timestamp in the first column; <see cref="LogFileFormatter"/> indents
/// everything else.
/// </summary>
public static partial class LogFileReader
{
    // The first stretch read takes this much of a file, each further one four times as much, up to
    // the budget: a page of entries is usually in the last few hundred KB of a day's file.
    private const long FirstReadBytes = 64 * 1024;

    // "2026-10-16 06:30:38.268 +02:00 [WRN] ": the 37 characters an entry's first line starts
    // with, the first 30 its time and characters 32 to 34 its level.
    private const int PrefixLength = 37;
    private const int TimeLength = 30;
    private const int LevelStart = 32;
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";

    private static readonly byte[] Indent = Encoding.ASCII.GetBytes(LogFileFormatter.Indent);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[[A-Z]{3}\] \z")]
    private static partial Regex EntryPrefix();

    // What Serilog names a day's file and the ones it rolls on to: api-20261016.log, api-20261016_001.log.
    [GeneratedRegex(@"^(?<source>[a-z]+)-\d{8}(?:_\d{3,})?\.log\z")]
    private static partial Regex FileName();

    /// <summary>
    /// A page of the entries <paramref name="filter"/> lets through, newest first, at most
    /// <paramref name="limit"/>, from the files in <paramref name="directories"/> (by service).
    /// Without a cursor it is the newest page. With <paramref name="before"/> it is the page
    /// older than the one that cursor came with, from the services it names. With
    /// <paramref name="after"/> it is what was written since that cursor's page, unless that is
    /// more than a page: then it is the newest page again, marked <see cref="LogPage.Reset"/>.
    /// Each service's read stops after about <paramref name="maxBytesPerSource"/>; a page cut
    /// short by that still has an older cursor, to look further back.
    /// </summary>
    public static LogPage ReadPage(
        IReadOnlyDictionary<string, string> directories, LogFilter filter, int limit, long maxBytesPerSource,
        LogCursor? before = null, LogCursor? after = null)
    {
        var scans = directories
            .Where(d => before is null || before.Positions.ContainsKey(d.Key))
            .Select(d => Scan(d.Value, d.Key, filter, limit, maxBytesPerSource, before?.At(d.Key), after?.At(d.Key)))
            .ToList();

        // Merged newest first, each service's entries kept in the order it wrote them, so what the
        // page takes of each is all of its entries down to one point: where its next page starts.
        var taken = new int[scans.Count];
        var entries = new List<LogEntry>(limit);
        while (entries.Count < limit)
        {
            var next = -1;
            for (var i = 0; i < scans.Count; i++)
            {
                if (taken[i] < scans[i].Entries.Count
                    && (next < 0 || scans[i].Entries[taken[i]].Time > scans[next].Entries[taken[next]].Time))
                    next = i;
            }
            if (next < 0) break;
            entries.Add(scans[next].Entries[taken[next]++]);
        }

        var whole = taken.Select((n, i) => n == scans[i].Entries.Count).ToList();
        var reset = before is null && (after is null || whole.Contains(false) || scans.Any(s => !s.Complete));
        var older = new Dictionary<string, LogPosition>(StringComparer.Ordinal);
        for (var i = 0; i < scans.Count; i++)
        {
            var scan = scans[i];
            var stop = whole[i] ? scan.Next
                : taken[i] > 0 ? scan.Entries[taken[i] - 1].Position
                : scan.From;
            if (stop is { } position) older[scan.Source] = position;
        }
        var newer = scans
            .Where(s => s.From is not null)
            .ToDictionary(s => s.Source, s => s.From!.Value, StringComparer.Ordinal);

        return new LogPage(
            entries,
            (reset || before is not null) && older.Count > 0 ? new LogCursor(older) : null,
            before is null && newer.Count > 0 ? new LogCursor(newer) : null,
            reset);
    }

    /// <summary>The service a log file belongs to, by its name, or null for a file Serilog did not name.</summary>
    internal static string? SourceOf(string fileName) =>
        FileName().Match(fileName) is { Success: true } match ? match.Groups["source"].Value : null;

    /// <summary>
    /// The entries <paramref name="filter"/> lets through in <paramref name="data"/>, a stretch of
    /// <paramref name="file"/> starting at byte <paramref name="origin"/>, in the order they were
    /// written. A line that starts no entry belongs to the one before it. With
    /// <paramref name="skipFirstLine"/> the stretch may start mid-line, so its first line is left
    /// out. <paramref name="firstStart"/> is where its first entry starts, let through or not. An
    /// entry below the filter's level is passed over without being decoded.
    /// </summary>
    internal static List<LogEntry> Parse(
        ReadOnlySpan<byte> data, string source, string file, long origin, bool skipFirstLine, LogFilter filter,
        out long? firstStart)
    {
        var entries = new List<LogEntry>();
        LogEntry? current = null;
        StringBuilder? more = null;
        Span<char> prefix = stackalloc char[PrefixLength];
        firstStart = null;

        void Finish()
        {
            if (current is null) return;
            var entry = more is { Length: > 0 } ? current with { Exception = more.ToString().TrimEnd() } : current;
            if (filter.Allows(entry)) entries.Add(entry);
            current = null;
            more = null;
        }

        var offset = 0;
        if (skipFirstLine)
        {
            var firstBreak = data.IndexOf((byte)'\n');
            offset = firstBreak < 0 ? data.Length : firstBreak + 1;
        }

        while (offset < data.Length)
        {
            var rest = data[offset..];
            var lineEnd = rest.IndexOf((byte)'\n');
            var line = lineEnd < 0 ? rest : rest[..lineEnd];
            var lineStart = origin + offset;
            offset += lineEnd < 0 ? rest.Length : lineEnd + 1;
            if (line is [.., (byte)'\r']) line = line[..^1];

            if (TryReadPrefix(line, prefix, out var level))
            {
                Finish();
                firstStart ??= lineStart;
                if (level < filter.MinimumLevel) continue;
                var time = DateTimeOffset.ParseExact(prefix[..TimeLength], TimeFormat, CultureInfo.InvariantCulture);
                current = new LogEntry(time, level, source, Encoding.UTF8.GetString(line[PrefixLength..]), null,
                    new LogPosition(file, lineStart));
            }
            else if (current is not null && line.Length > 0)
            {
                (more ??= new StringBuilder())
                    .Append(Encoding.UTF8.GetString(line.StartsWith(Indent) ? line[Indent.Length..] : line))
                    .Append('\n');
            }
        }

        Finish();
        return entries;
    }

    // One service's entries starting in [after, before), newest first, until `limit` of them are
    // found or `maxBytes` are read. A missing bound is the end of its history on that side.
    private static LogScan Scan(
        string directory, string source, LogFilter filter, int limit, long maxBytes, LogPosition? before, LogPosition? after)
    {
        var found = new List<LogEntry>();
        var from = before;
        if (!Directory.Exists(directory)) return new LogScan(source, found, from ?? after, after, Complete: true);

        // Newest first: the date sorts by day, and a day's _001, _002 files after its first one.
        var files = Directory.EnumerateFiles(directory, $"{source}-*.log")
            .Select(path => Path.GetFileName(path))
            .Where(name => SourceOf(name) == source)
            .OrderByDescending(name => name, StringComparer.Ordinal)
            .ToList();
        // The file the lower bound is in: the cursor's, or the oldest there is.
        var bottom = after?.File ?? files.LastOrDefault();
        var budget = maxBytes;
        var size = FirstReadBytes;

        foreach (var name in files)
        {
            if (before is { } upper && string.CompareOrdinal(name, upper.File) > 0) continue;
            if (after is { } lower && string.CompareOrdinal(name, lower.File) < 0) break;

            using var handle = File.OpenHandle(
                Path.Combine(directory, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = RandomAccess.GetLength(handle);
            var end = before?.File == name ? Math.Min(before.Value.Offset, length) : length;
            var floor = after?.File == name ? Math.Min(after.Value.Offset, end) : 0;
            // Inside an entry longer than what was read: its start is looked for past the budget,
            // so no page ever holds half an entry, and every page gets somewhere.
            var seekingStart = false;

            while (end > floor)
            {
                if (budget <= 0 && !seekingStart)
                    return new LogScan(source, found, from, new LogPosition(name, end), Complete: false);

                var take = (int)Math.Min(size, end - floor);
                if (!seekingStart) take = (int)Math.Min(take, budget);
                var start = end - take;
                budget -= take;
                var buffer = ArrayPool<byte>.Shared.Rent(take);
                try
                {
                    var data = Read(handle, buffer.AsSpan(0, take), start);
                    if (from is null)
                    {
                        // The newest file's last line may still be being written: it waits for the next read.
                        var complete = data.LastIndexOf((byte)'\n') + 1;
                        if (complete == 0 && start > floor)
                        {
                            seekingStart = true;
                            size *= 4;
                            continue;
                        }

                        data = data[..complete];
                        end = start + complete;
                        from = new LogPosition(name, end);
                    }

                    var entries = Parse(data, source, name, start, skipFirstLine: start > floor, filter, out var firstStart);
                    if (firstStart is null && start > floor)
                    {
                        seekingStart = true;
                        size *= 4;
                        continue;
                    }

                    seekingStart = false;
                    for (var i = entries.Count - 1; i >= 0; i--)
                    {
                        found.Add(entries[i]);
                        if (found.Count < limit) continue;
                        // Full, and just as well at the lower bound if nothing else was there to find.
                        return i == 0 && start == floor && name == bottom
                            ? new LogScan(source, found, from, after, Complete: true)
                            : new LogScan(source, found, from, entries[i].Position, Complete: false);
                    }

                    end = start > floor ? firstStart!.Value : floor;
                    size = Math.Min(size * 4, maxBytes);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            if (after?.File == name) break;
        }

        return new LogScan(source, found, from ?? after, after, Complete: true);
    }

    private static ReadOnlySpan<byte> Read(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = RandomAccess.Read(handle, buffer[read..], offset + read);
            if (count == 0) break;
            read += count;
        }
        return buffer[..read];
    }

    // Whether the line starts an entry, and at what level. Its first characters land in `prefix`,
    // for the time to be read from; the rest is decoded only for an entry the filter keeps.
    private static bool TryReadPrefix(ReadOnlySpan<byte> line, Span<char> prefix, out LogLevelName level)
    {
        level = default;
        if (line.Length < PrefixLength || !char.IsAsciiDigit((char)line[0])) return false;
        Encoding.Latin1.GetChars(line[..PrefixLength], prefix);
        if (!EntryPrefix().IsMatch(prefix)) return false;

        LogLevelName? named = prefix.Slice(LevelStart, 3) switch
        {
            "VRB" => LogLevelName.Verbose,
            "DBG" => LogLevelName.Debug,
            "INF" => LogLevelName.Information,
            "WRN" => LogLevelName.Warning,
            "ERR" => LogLevelName.Error,
            "FTL" => LogLevelName.Fatal,
            _ => null,
        };
        level = named.GetValueOrDefault();
        return named is not null;
    }

    // What one service's read found, newest first. From is where it started, the top of what it
    // covers; Next is where it stopped, null once nothing older is left. Complete is whether it
    // reached its lower bound rather than the limit or the budget.
    private sealed record LogScan(string Source, List<LogEntry> Entries, LogPosition? From, LogPosition? Next, bool Complete);
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
public sealed record LogEntry(
    DateTimeOffset Time, LogLevelName Level, string Source, string Message, string? Exception,
    [property: JsonIgnore] LogPosition Position)
{
    /// <summary>Where the entry starts: unique, and the same on every read, so the log viewer keys its list on it.</summary>
    public string Id => Position.ToString();
}

/// <summary>Which entries to show: from <paramref name="MinimumLevel"/> up, and containing <paramref name="Search"/> if given.</summary>
public sealed record LogFilter(LogLevelName MinimumLevel, string? Search)
{
    public bool Allows(LogEntry entry) =>
        entry.Level >= MinimumLevel
        && (string.IsNullOrWhiteSpace(Search)
            || entry.Message.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || entry.Exception?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true);
}

/// <summary>
/// A point in one service's log history: byte <paramref name="Offset"/> of <paramref name="File"/>.
/// Its older files, and this one up to the offset, come before it.
/// </summary>
public readonly record struct LogPosition(string File, long Offset)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{File}:{Offset}");
}

/// <summary>
/// Where a page stops in each service's files, as the log viewer hands it back for the next one:
/// "api-20261016.log:52311,worker-20261016.log:1830". A file's name says whose it is. Only
/// compared with the names of the files there are, never opened by.
/// </summary>
public sealed class LogCursor(IReadOnlyDictionary<string, LogPosition> positions)
{
    public IReadOnlyDictionary<string, LogPosition> Positions { get; } = positions;

    public LogPosition? At(string source) => Positions.TryGetValue(source, out var position) ? position : null;

    public override string ToString() => string.Join(',', Positions.Values);

    public static bool TryParse(string? text, [NotNullWhen(true)] out LogCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(text)) return false;

        var positions = new Dictionary<string, LogPosition>(StringComparer.Ordinal);
        foreach (var part in text.Split(','))
        {
            var colon = part.LastIndexOf(':');
            if (colon < 0
                || !long.TryParse(part.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                || LogFileReader.SourceOf(part[..colon]) is not { } source
                || !positions.TryAdd(source, new LogPosition(part[..colon], offset)))
                return false;
        }

        cursor = new LogCursor(positions);
        return true;
    }
}

/// <summary>
/// A page of entries, newest first. <paramref name="Older"/> reads on from its oldest entry, null
/// once nothing older is left (and on a live read that did not reset); <paramref name="Newer"/>
/// reads what is written after it (null on an older page). <paramref name="Reset"/> says the
/// entries replace what the viewer holds rather than extend it.
/// </summary>
public sealed record LogPage(IReadOnlyList<LogEntry> Entries, LogCursor? Older, LogCursor? Newer, bool Reset);
