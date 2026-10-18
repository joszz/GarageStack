using System.Text;
using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public sealed class LogFileReaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("garagestack-logs-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void Write(string file, params string[] lines) =>
        File.WriteAllText(Path.Combine(_directory, file), string.Join("\n", lines) + "\n");

    private void Append(string file, string text) =>
        File.AppendAllText(Path.Combine(_directory, file), text);

    private static readonly LogFilter Everything = new(LogLevelName.Verbose, null);

    private const long Plenty = 1_000_000;

    private static string Line(int minute, string level, string message) =>
        $"2026-10-16 10:{minute:00}:00.000 +02:00 [{level}] {message}";

    private LogPage Read(
        LogFilter? filter = null, int limit = 10, long maxBytes = Plenty, LogCursor? before = null, LogCursor? after = null,
        params string[] sources) =>
        LogFileReader.ReadPage(
            (sources.Length > 0 ? sources : ["api"]).ToDictionary(s => s, _ => _directory),
            filter ?? Everything, limit, maxBytes, before, after);

    private static List<LogEntry> Parse(string text, string source) =>
        LogFileReader.Parse(Encoding.UTF8.GetBytes(text), source, $"{source}-20261016.log", 0, skipFirstLine: false, Everything, out _);

    [Fact]
    public void Parse_KeepsAnExceptionsLinesWithItsEntry()
    {
        var entries = Parse(
            """
            2026-10-16 06:30:38.268 +02:00 [INF] Started
            2026-10-16 06:31:00.157 +02:00 [ERR] Failed to persist telemetry
            System.InvalidOperationException: boom
               at GarageStack.Worker.Thing()
            2026-10-16 06:31:01.000 +02:00 [WRN] Carrying on
            """,
            "worker");

        Assert.Equal([LogLevelName.Information, LogLevelName.Error, LogLevelName.Warning], entries.Select(e => e.Level));
        Assert.Equal("Failed to persist telemetry", entries[1].Message);
        Assert.Contains("InvalidOperationException: boom", entries[1].Exception);
        Assert.Contains("at GarageStack.Worker.Thing()", entries[1].Exception);
        Assert.Null(entries[0].Exception);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 6, 30, 38, 268, TimeSpan.FromHours(2)), entries[0].Time);
    }

    // Its file and where in it the entry starts: the log viewer keys its list on that.
    [Fact]
    public void Parse_GivesEachEntryWhereItStarts()
    {
        var first = Line(0, "INF", "first");

        var entries = Parse(first + "\n" + Line(1, "INF", "second") + "\n", "api");

        Assert.Equal(["api-20261016.log:0", $"api-20261016.log:{first.Length + 1}"], entries.Select(e => e.Id));
    }

    // A day's file rolled at its size cap carries on in _001: that one is newer than the day's first.
    [Fact]
    public void ReadPage_IsNewestFirst_AcrossDaysAndRolledFiles()
    {
        Write("api-20261015.log", "2026-10-15 10:00:00.000 +02:00 [INF] yesterday");
        Write("api-20261016.log", "2026-10-16 10:00:00.000 +02:00 [INF] this morning");
        Write("api-20261016_001.log", "2026-10-16 15:00:00.000 +02:00 [INF] this afternoon");
        Write("worker-20261016.log", "2026-10-16 16:00:00.000 +02:00 [INF] not the API's");
        Write("api-notes.log", "2026-10-16 17:00:00.000 +02:00 [INF] not a file Serilog wrote");

        var page = Read();

        Assert.Equal(["this afternoon", "this morning", "yesterday"], page.Entries.Select(e => e.Message));
        Assert.True(page.Reset);
        Assert.Null(page.Older);
    }

    [Fact]
    public void ReadPage_FiltersByLevelAndSearch_AndStopsAtTheLimit()
    {
        Write("api-20261016.log",
            Line(0, "INF", "MQTT connected"),
            Line(1, "WRN", "MQTT broker connection failed"),
            Line(2, "ERR", "Postgres down"),
            Line(3, "WRN", "MQTT broker connection failed again"));

        var warnings = Read(new LogFilter(LogLevelName.Warning, null));
        var mqtt = Read(new LogFilter(LogLevelName.Information, "mqtt"));
        var newest = Read(limit: 1);

        Assert.Equal(3, warnings.Entries.Count);
        Assert.Equal(3, mqtt.Entries.Count);
        Assert.Equal("MQTT broker connection failed again", Assert.Single(newest.Entries).Message);
        Assert.NotNull(newest.Older);
    }

    // Only the end of a file is read; the first line of that end is half an entry and is left out,
    // and the next page picks it up whole.
    [Fact]
    public void ReadPage_ReadsOnlyTheEnd_WithoutAHalfEntry_AndTheNextPageGoesOn()
    {
        Write("api-20261016.log",
            Line(0, "INF", "an old entry with a long message that falls outside the budget"),
            Line(1, "INF", "the newest"));

        var first = Read(maxBytes: 60);
        var second = Read(maxBytes: 60, before: first.Older);

        Assert.Equal("the newest", Assert.Single(first.Entries).Message);
        Assert.StartsWith("an old entry", Assert.Single(second.Entries).Message);
        Assert.EndsWith("outside the budget", second.Entries[0].Message);
        Assert.Null(second.Older);
        Assert.False(second.Reset);
    }

    [Fact]
    public void ReadPage_MissingDirectory_IsEmpty()
    {
        var page = LogFileReader.ReadPage(
            new Dictionary<string, string> { ["api"] = Path.Combine(_directory, "nope") }, Everything, 10, 1000);

        Assert.Empty(page.Entries);
        Assert.Null(page.Older);
        Assert.Null(page.Newer);
    }

    // A page usually sits in a file's last few KB, so the reader starts small; one the filter has
    // to look further back for still turns up, as long as it is within the budget.
    [Fact]
    public void ReadPage_LooksFurtherBack_UntilThePageIsFull()
    {
        var noise = Enumerable.Range(0, 5000).Select(i => $"2026-10-16 10:{i / 600 % 60:00}:{i / 10 % 60:00}.{i % 10:000} +02:00 [INF] request {i} answered in a few milliseconds");
        Write("api-20261016.log", ["2026-10-16 09:59:00.000 +02:00 [WRN] the one warning", .. noise]);

        var warnings = Read(new LogFilter(LogLevelName.Warning, null), limit: 5, maxBytes: 8L * 1024 * 1024);

        Assert.Equal("the one warning", Assert.Single(warnings.Entries).Message);
    }

    // Page after page, small and with a budget that cuts some short, through both services' files:
    // every entry turns up once, newest first, and the last page says nothing older is left.
    [Fact]
    public void ReadPage_PagesThroughTheWholeHistory_WithoutGapsOrRepeats()
    {
        Write("api-20261015.log", [.. Enumerable.Range(0, 20).Select(i => Line(i, i % 3 == 0 ? "WRN" : "INF", $"api old {i}"))]);
        Write("api-20261016.log", [.. Enumerable.Range(20, 20).Select(i => Line(i, "INF", $"api new {i}"))]);
        Write("worker-20261016.log",
            [.. Enumerable.Range(0, 40).Select(i => Line(i, "INF", $"worker {i}") + (i % 7 == 0 ? "\n  System.Exception: boom\n    at Somewhere()" : ""))]);
        var expected = Directory.GetFiles(_directory)
            .SelectMany(f => Parse(File.ReadAllText(f), Path.GetFileName(f).Split('-')[0]))
            .Select(e => e.Message)
            .Order()
            .ToList();

        var seen = new List<LogEntry>();
        LogCursor? before = null;
        var pages = 0;
        do
        {
            var page = Read(limit: 3, maxBytes: 300, before: before, sources: ["api", "worker"]);
            seen.AddRange(page.Entries);
            before = page.Older;
            Assert.True(++pages < 200, "paging never ended");
        }
        while (before is not null);

        Assert.Equal(expected, seen.Select(e => e.Message).Order());
        Assert.Equal(seen.Count, seen.Select(e => e.Id).Distinct().Count());
        Assert.Equal(seen.Select(e => e.Time).OrderDescending(), seen.Select(e => e.Time));
        Assert.Contains(seen, e => e.Exception == "System.Exception: boom\n  at Somewhere()");
    }

    // Live mode reads only what was written since, here into the day's file and the one it rolled on to.
    [Fact]
    public void ReadPage_After_IsWhatWasWrittenSince()
    {
        Write("api-20261016.log", Line(0, "INF", "already shown"));
        var first = Read();

        Append("api-20261016.log", Line(1, "INF", "since") + "\n");
        Write("api-20261016_001.log", Line(2, "WRN", "rolled on"));
        var live = Read(after: first.Newer);
        var quiet = Read(after: live.Newer);

        Assert.False(live.Reset);
        Assert.Equal(["rolled on", "since"], live.Entries.Select(e => e.Message));
        Assert.Null(live.Older);
        Assert.False(quiet.Reset);
        Assert.Empty(quiet.Entries);
    }

    // More new entries than a page holds leave a gap behind them: the page starts over, and its
    // older cursor still reaches the entries shown before.
    [Fact]
    public void ReadPage_After_MoreThanAPage_StartsOver()
    {
        Write("api-20261016.log", Line(0, "INF", "already shown"));
        var first = Read(limit: 2);

        Append("api-20261016.log", string.Concat(Enumerable.Range(1, 3).Select(i => Line(i, "INF", $"burst {i}") + "\n")));
        var live = Read(limit: 2, after: first.Newer);
        var rest = Read(limit: 2, before: live.Older);

        Assert.True(live.Reset);
        Assert.Equal(["burst 3", "burst 2"], live.Entries.Select(e => e.Message));
        Assert.Equal(["burst 1", "already shown"], rest.Entries.Select(e => e.Message));
    }

    // The service may be halfway through writing a line: it is left for the read after.
    [Fact]
    public void ReadPage_LeavesAHalfWrittenLine_ForTheNextRead()
    {
        Append("api-20261016.log", Line(0, "INF", "done") + "\n" + Line(1, "INF", "half"));
        var first = Read();

        Append("api-20261016.log", " written\n");
        var live = Read(after: first.Newer);

        Assert.Equal("done", Assert.Single(first.Entries).Message);
        Assert.Equal("half written", Assert.Single(live.Entries).Message);
    }

    [Fact]
    public void Cursor_RoundTrips()
    {
        Assert.True(LogCursor.TryParse("api-20261016_001.log:52311,worker-20261015.log:0", out var cursor));

        Assert.Equal(new LogPosition("api-20261016_001.log", 52311), cursor.At("api"));
        Assert.Equal(new LogPosition("worker-20261015.log", 0), cursor.At("worker"));
        Assert.Equal("api-20261016_001.log:52311,worker-20261015.log:0", cursor.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("api-20261016.log")]
    [InlineData("api-20261016.log:-1")]
    [InlineData("api-20261016.log: 5")]
    [InlineData("../secrets-20261016.log:0")]
    [InlineData("api-notes.log:0")]
    [InlineData("api-20261016.log:1,api-20261015.log:2")]
    public void Cursor_RefusesAnythingButFileNamesAndOffsets(string text)
    {
        Assert.False(LogCursor.TryParse(text, out _));
    }

    // What the file sink writes reads back as one entry, whatever line breaks the entry quotes: an
    // identity provider's error description posing as a log line stays inside its entry.
    [Fact]
    public void TheFileFormat_KeepsQuotedLineBreaksInsideTheirEntry()
    {
        var parser = new Serilog.Parsing.MessageTemplateParser();
        var forged = "denied\n2026-10-16 06:31:00.157 +02:00 [ERR] forged entry";
        var logEvent = new Serilog.Events.LogEvent(
            new DateTimeOffset(2026, 10, 16, 6, 30, 0, TimeSpan.FromHours(2)),
            Serilog.Events.LogEventLevel.Warning,
            new InvalidOperationException("boom\n2026-10-16 06:32:00.000 +02:00 [ERR] forged too"),
            parser.Parse("OIDC sign-in failed: {Error}"),
            [new Serilog.Events.LogEventProperty("Error", new Serilog.Events.ScalarValue(forged))]);
        using var output = new StringWriter();

        new LogFileFormatter().Format(logEvent, output);
        var entries = Parse(output.ToString(), "api");

        var entry = Assert.Single(entries);
        Assert.Equal(LogLevelName.Warning, entry.Level);
        Assert.StartsWith("OIDC sign-in failed: denied", entry.Message);
        Assert.Contains("[ERR] forged entry", entry.Exception);
        Assert.Contains("InvalidOperationException: boom", entry.Exception);
        Assert.Contains("[ERR] forged too", entry.Exception);
    }
}
