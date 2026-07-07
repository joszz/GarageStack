namespace GarageStack.Core.Helpers;

public enum StateTransition
{
    /// <summary>No prior value for this VIN to compare against; treat as a no-op.</summary>
    FirstObservation,
    Unchanged,
    TurnedOn,
    TurnedOff,
}

/// <summary>
/// Interprets a <see cref="VinStateTracker{T}.TryUpdate"/> result (hadPrevious/previous/current)
/// as an on/off transition. Shared by every checker built on top of a boolean VinStateTracker
/// (engine running, charging) so the on/off/first-seen branching isn't reimplemented per caller.
/// </summary>
public static class BoolTransitionDetector
{
    public static StateTransition Detect(bool hadPrevious, bool? previous, bool current)
    {
        if (!hadPrevious || previous is null) return StateTransition.FirstObservation;
        if (current && previous == false) return StateTransition.TurnedOn;
        if (!current && previous == true) return StateTransition.TurnedOff;
        return StateTransition.Unchanged;
    }
}
