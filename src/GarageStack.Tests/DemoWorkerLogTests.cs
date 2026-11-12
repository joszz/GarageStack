using System.Text;
using GarageStack.Core.Helpers;
using GarageStack.Data.Demo;

namespace GarageStack.Tests;

public class DemoWorkerLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 21, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void EveryLine_IsAnEntryTheLogViewerReads_OldestFirst_AndBeforeNow()
    {
        var lines = DemoWorkerLog.Lines(Now);
        var text = string.Join("\n", lines) + "\n";

        var entries = LogFileReader.Parse(Encoding.UTF8.GetBytes(text), "worker", "worker-20261112.log", 0,
            skipFirstLine: false, new LogFilter(LogLevelName.Verbose, null), out _);

        Assert.Equal(lines.Count, entries.Count);
        Assert.Equal(entries.OrderBy(e => e.Time).Select(e => e.Message), entries.Select(e => e.Message));
        Assert.All(entries, e => Assert.True(e.Time < Now));
        Assert.Contains(entries, e => e.Level == LogLevelName.Warning);
    }

    [Fact]
    public void TheDemoVin_IsMasked_AsTheWorkerMasksItInItsLogs()
    {
        var text = string.Join("\n", DemoWorkerLog.Lines(Now));

        Assert.DoesNotContain("DEMOVIN0000012345", text, StringComparison.Ordinal);
        Assert.Contains(LogRedaction.Vin("DEMOVIN0000012345"), text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFolder_HoldsOneDaysFile()
    {
        var file = Assert.Single(Directory.GetFiles(DemoWorkerLog.Directory));

        Assert.Matches(@"worker-\d{8}\.log$", file);
    }
}
