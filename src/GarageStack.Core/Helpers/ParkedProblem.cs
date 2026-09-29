using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Tells a car left open from a driver still busy at it. The gateway polls a parked car for about
/// ten minutes and then leaves it alone until it next wakes, so the last poll of that window can
/// land just as the driver walks back to fetch something: one unlocked reading, then silence until
/// the next drive. A car that was really left open shows the problem poll after poll.
/// </summary>
public static class ParkedProblem
{
    /// <summary>How long the polls must keep showing a problem before it counts.</summary>
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// True when the newest reading shows the problem, and so does every reading back to one at
    /// least <see cref="MinimumDuration"/> older than it.
    /// </summary>
    /// <param name="newestFirst">The car's status readings, newest first.</param>
    /// <param name="shows">Whether a reading shows the problem.</param>
    public static bool Persists(IReadOnlyList<StatusReading> newestFirst, Func<TelemetrySnapshot, bool> shows)
    {
        if (newestFirst.Count == 0 || !shows(newestFirst[0].State)) return false;

        var latest = newestFirst[0].ArrivedAt;
        foreach (var reading in newestFirst.Skip(1))
        {
            if (!shows(reading.State)) return false;
            if (latest - reading.ArrivedAt >= MinimumDuration) return true;
        }

        return false;
    }
}
