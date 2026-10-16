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
}
