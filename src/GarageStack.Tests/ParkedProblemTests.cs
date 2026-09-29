using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class ParkedProblemTests
{
    private static readonly DateTime ParkedAt = new(2026, 9, 23, 14, 57, 0, DateTimeKind.Utc);

    // Polls at the given minutes after parking, each unlocked or not. Written oldest first and
    // handed over newest first, the way the repository returns them.
    private static List<StatusReading> Polls(params (double Minutes, bool Unlocked)[] oldestFirst) =>
        [.. oldestFirst
            .Select(p => new StatusReading(ParkedAt.AddMinutes(p.Minutes), new TelemetrySnapshot { IsLocked = !p.Unlocked }))
            .Reverse()];

    private static bool Unlocked(TelemetrySnapshot s) => s.IsLocked is false;

    [Fact]
    public void Persists_ShownOnEveryPollForFiveMinutes_True()
    {
        Assert.True(ParkedProblem.Persists(Polls((0, true), (2.5, true), (5, true)), Unlocked));
    }

    [Fact]
    public void Persists_ShownForUnderFiveMinutes_False()
    {
        Assert.False(ParkedProblem.Persists(Polls((0, false), (2.5, true), (7, true)), Unlocked));
    }

    [Fact]
    public void Persists_APollWithoutTheProblemInBetween_False()
    {
        Assert.False(ParkedProblem.Persists(Polls((0, true), (2.5, false), (5, true), (7.5, true)), Unlocked));
    }

    [Fact]
    public void Persists_NewestPollWithoutTheProblem_False()
    {
        Assert.False(ParkedProblem.Persists(Polls((0, true), (5, true), (10, false)), Unlocked));
    }

    [Fact]
    public void Persists_NoPolls_False()
    {
        Assert.False(ParkedProblem.Persists([], Unlocked));
    }
}
