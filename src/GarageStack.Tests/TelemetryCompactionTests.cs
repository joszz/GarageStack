using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TelemetryCompactionTests
{
    // On a quarter-hour, so T0.AddMinutes(15) starts the next window.
    private static readonly DateTime T0 = new(2025, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AWindow_KeepsItsLastRow_FilledWithTheNewestEarlierValueOfEachField()
    {
        var first = new TelemetrySnapshot { Id = 1, RecordedAt = T0, FuelLevelPercent = 50, OdometerKm = 1000, EngineRunning = true };
        var second = new TelemetrySnapshot { Id = 2, RecordedAt = T0.AddMinutes(5), FuelLevelPercent = 49, BatteryVoltage = 12.6 };
        var last = new TelemetrySnapshot { Id = 3, RecordedAt = T0.AddMinutes(14), Latitude = 52.0, EngineRunning = false };

        var folded = TelemetryCompaction.Fold([last, first, second]);

        Assert.Equal([1L, 2L], folded.Select(r => r.Id).Order());
        Assert.Equal(T0.AddMinutes(14), last.RecordedAt);
        Assert.Equal(49.0, last.FuelLevelPercent);
        Assert.Equal(1000.0, last.OdometerKm);
        Assert.Equal(12.6, last.BatteryVoltage);
        Assert.Equal(52.0, last.Latitude);
        Assert.False(last.EngineRunning);
    }

    [Fact]
    public void RowsInDifferentWindows_StaySeparate()
    {
        var rows = new[]
        {
            new TelemetrySnapshot { Id = 1, RecordedAt = T0.AddMinutes(14).AddSeconds(59), FuelLevelPercent = 50 },
            new TelemetrySnapshot { Id = 2, RecordedAt = T0.AddMinutes(15), FuelLevelPercent = 49 },
        };

        Assert.Empty(TelemetryCompaction.Fold(rows));
        Assert.Equal(50.0, rows[0].FuelLevelPercent);
    }

    [Fact]
    public void ACounterReset_CutsTheWindow_SoTheTotalBeforeItSurvives()
    {
        // The day's counters reach 125 km and 6.5 L, then start again from zero within the window.
        var early = new TelemetrySnapshot { Id = 1, RecordedAt = T0.AddMinutes(1), MileageOfTheDay = 120, PowerUsageOfDay = 640 };
        var dayTotal = new TelemetrySnapshot { Id = 2, RecordedAt = T0.AddMinutes(4), MileageOfTheDay = 125, PowerUsageOfDay = 650 };
        var reset = new TelemetrySnapshot { Id = 3, RecordedAt = T0.AddMinutes(6), MileageOfTheDay = 0 };
        var after = new TelemetrySnapshot { Id = 4, RecordedAt = T0.AddMinutes(10), FuelLevelPercent = 40 };

        var folded = TelemetryCompaction.Fold([early, dayTotal, reset, after]);

        Assert.Equal([1L, 3L], folded.Select(r => r.Id).Order());
        Assert.Equal(125.0, dayTotal.MileageOfTheDay);
        Assert.Equal(650.0, dayTotal.PowerUsageOfDay);
        Assert.Equal(0.0, after.MileageOfTheDay);
        Assert.Null(after.PowerUsageOfDay);
    }

    [Fact]
    public void CompactedRows_ComeOutUnchanged()
    {
        var rows = new List<TelemetrySnapshot>
        {
            new() { Id = 1, RecordedAt = T0, MileageOfTheDay = 120 },
            new() { Id = 2, RecordedAt = T0.AddMinutes(4), MileageOfTheDay = 125 },
            new() { Id = 3, RecordedAt = T0.AddMinutes(6), MileageOfTheDay = 0 },
            new() { Id = 4, RecordedAt = T0.AddMinutes(20), OdometerKm = 1000 },
        };
        var kept = rows.Except(TelemetryCompaction.Fold(rows)).ToList();

        Assert.Empty(TelemetryCompaction.Fold(kept));
    }

    [Fact]
    public void WindowStart_IsTheQuarterHourATimeFallsIn()
    {
        Assert.Equal(T0.AddMinutes(15), TelemetryCompaction.WindowStart(T0.AddMinutes(29).AddSeconds(59)));
        Assert.Equal(DateTimeKind.Utc, TelemetryCompaction.WindowStart(T0.AddMinutes(1)).Kind);
    }
}
