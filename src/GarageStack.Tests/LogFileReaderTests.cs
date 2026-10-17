using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public sealed class LogFileReaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("garagestack-logs-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void Write(string file, params string[] lines) =>
        File.WriteAllText(Path.Combine(_directory, file), string.Join("\n", lines) + "\n");

    private static readonly LogFilter Everything = new(LogLevelName.Verbose, null);

    [Fact]
    public void Parse_KeepsAnExceptionsLinesWithItsEntry()
    {
        var entries = LogFileReader.Parse(
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

    // A day's file rolled at its size cap carries on in _001: that one is newer than the day's first.
    [Fact]
    public void ReadRecent_IsNewestFirst_AcrossDaysAndRolledFiles()
    {
        Write("api-20261015.log", "2026-10-15 10:00:00.000 +02:00 [INF] yesterday");
        Write("api-20261016.log", "2026-10-16 10:00:00.000 +02:00 [INF] this morning");
        Write("api-20261016_001.log", "2026-10-16 15:00:00.000 +02:00 [INF] this afternoon");
        Write("worker-20261016.log", "2026-10-16 16:00:00.000 +02:00 [INF] not the API's");

        var entries = LogFileReader.ReadRecent(_directory, "api", Everything, limit: 10, maxBytes: 1_000_000);

        Assert.Equal(["this afternoon", "this morning", "yesterday"], entries.Select(e => e.Message));
    }

    [Fact]
    public void ReadRecent_FiltersByLevelAndSearch_AndStopsAtTheLimit()
    {
        Write("api-20261016.log",
            "2026-10-16 10:00:00.000 +02:00 [INF] MQTT connected",
            "2026-10-16 10:01:00.000 +02:00 [WRN] MQTT broker connection failed",
            "2026-10-16 10:02:00.000 +02:00 [ERR] Postgres down",
            "2026-10-16 10:03:00.000 +02:00 [WRN] MQTT broker connection failed again");

        var warnings = LogFileReader.ReadRecent(_directory, "api", new LogFilter(LogLevelName.Warning, null), 10, 1_000_000);
        var mqtt = LogFileReader.ReadRecent(_directory, "api", new LogFilter(LogLevelName.Information, "mqtt"), 10, 1_000_000);
        var newest = LogFileReader.ReadRecent(_directory, "api", Everything, limit: 1, maxBytes: 1_000_000);

        Assert.Equal(3, warnings.Count);
        Assert.Equal(3, mqtt.Count);
        Assert.Equal("MQTT broker connection failed again", Assert.Single(newest).Message);
    }

    // Only the end of a file is read; the first line of that end is half an entry and is left out.
    [Fact]
    public void ReadRecent_ReadsOnlyTheEnd_WithoutAHalfEntry()
    {
        Write("api-20261016.log",
            "2026-10-16 10:00:00.000 +02:00 [INF] an old entry with a long message that falls outside the budget",
            "2026-10-16 10:01:00.000 +02:00 [INF] the newest");

        var entries = LogFileReader.ReadRecent(_directory, "api", Everything, limit: 10, maxBytes: 60);

        Assert.Equal("the newest", Assert.Single(entries).Message);
    }

    [Fact]
    public void ReadRecent_MissingDirectory_IsEmpty()
    {
        Assert.Empty(LogFileReader.ReadRecent(Path.Combine(_directory, "nope"), "api", Everything, 10, 1000));
    }

    // A page usually sits in a file's last few KB, so the reader starts small; one the filter has
    // to look further back for still turns up, as long as it is within the budget.
    [Fact]
    public void ReadRecent_LooksFurtherBack_UntilThePageIsFull()
    {
        var noise = Enumerable.Range(0, 5000).Select(i => $"2026-10-16 10:{i / 600 % 60:00}:{i / 10 % 60:00}.{i % 10:000} +02:00 [INF] request {i} answered in a few milliseconds");
        Write("api-20261016.log", ["2026-10-16 09:59:00.000 +02:00 [WRN] the one warning", .. noise]);

        var warnings = LogFileReader.ReadRecent(_directory, "api", new LogFilter(LogLevelName.Warning, null), limit: 5, maxBytes: 8L * 1024 * 1024);

        Assert.Equal("the one warning", Assert.Single(warnings).Message);
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
        var entries = LogFileReader.Parse(output.ToString(), "api");

        var entry = Assert.Single(entries);
        Assert.Equal(LogLevelName.Warning, entry.Level);
        Assert.StartsWith("OIDC sign-in failed: denied", entry.Message);
        Assert.Contains("[ERR] forged entry", entry.Exception);
        Assert.Contains("InvalidOperationException: boom", entry.Exception);
        Assert.Contains("[ERR] forged too", entry.Exception);
    }
}
