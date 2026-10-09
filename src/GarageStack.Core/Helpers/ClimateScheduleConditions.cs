using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>Whether a due climate schedule should run, given what the car last reported.</summary>
public static class ClimateScheduleConditions
{
    /// <summary>
    /// Why <paramref name="schedule"/> should not run now, or null when it should.
    /// <paramref name="exteriorC"/> is null when no recent reading is at hand; the schedule then
    /// runs, since a cold morning missed is worse than a mild one warmed.
    /// </summary>
    public static ClimateScheduleOutcome? SkipReason(ClimateSchedule schedule, bool? engineRunning, double? exteriorC)
    {
        // Someone is driving it: the cabin is theirs to set, and the car would refuse anyway.
        if (engineRunning == true) return ClimateScheduleOutcome.SkippedDriving;

        if (exteriorC is not { } outside) return null;
        if (schedule.OnlyBelowC is null && schedule.OnlyAboveC is null) return null;

        var coldEnough = outside < schedule.OnlyBelowC;
        var warmEnough = outside > schedule.OnlyAboveC;
        return coldEnough || warmEnough ? null : ClimateScheduleOutcome.SkippedTemperature;
    }
}
