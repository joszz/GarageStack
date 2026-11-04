namespace GarageStack.Core.Models;

/// <summary>
/// A point as the trip editor sends it: where, and when. The server decides its speed and
/// <see cref="TripPoint.Source"/>, so a client cannot pass a point it placed off as one the car
/// reported. Every field is nullable so a missing one is a validation error, not a binding failure.
/// </summary>
public sealed record TripPointInput(DateTimeOffset? RecordedAt, double? Latitude, double? Longitude);

/// <summary>
/// Where the car stood before or after a trip: a parked fix, or the neighbouring trip's end.
/// <paramref name="SuggestedAt"/> is the time to give a point placed there: when the car left it,
/// or when it got there.
/// </summary>
public sealed record TripStop(double Latitude, double Longitude, DateTime RecordedAt, DateTime SuggestedAt);

/// <summary>
/// What the trip editor needs to edit a saved trip's route.
/// </summary>
/// <param name="Id">The saved trip's id.</param>
/// <param name="Points">The trip's points as they are now.</param>
/// <param name="OriginalPoints">The points as recorded, when the trip was edited; null otherwise.</param>
/// <param name="EditedAt">When the route was last edited, or null when it is as recorded.</param>
/// <param name="PreviousStop">Where the car stood before the trip, or null when nothing says.</param>
/// <param name="NextStop">Where the car stood after it, or null when nothing says.</param>
/// <param name="Suggestion">
/// Whether <paramref name="PreviousStop"/> is far enough from the start, and near enough, that the
/// trip probably left from there: a trip saved before trips were anchored, which one tap can fix.
/// </param>
/// <param name="NotBefore">The end of the trip before it: no point may be earlier.</param>
/// <param name="NotAfter">The start of the trip after it, or the recording line: no point may be later.</param>
/// <param name="MaxPoints">The most points a save may send.</param>
public sealed record TripEditor(
    long Id,
    IReadOnlyList<TripPoint> Points,
    IReadOnlyList<TripPoint>? OriginalPoints,
    DateTime? EditedAt,
    TripStop? PreviousStop,
    TripStop? NextStop,
    bool Suggestion,
    DateTime? NotBefore,
    DateTime? NotAfter,
    int MaxPoints);

/// <summary>Why a set of points was refused, as a problem code and a message.</summary>
public sealed record TripEditProblem(string Code, string Message);

/// <summary>How a trip editor request went.</summary>
public enum TripEditOutcome
{
    Ok,

    /// <summary>The vehicle has no such saved trip.</summary>
    NotFound,

    /// <summary>The trip's points cannot be read, so there is nothing to edit.</summary>
    Unreadable,

    /// <summary>The points sent were refused; <see cref="TripEditResult.Problem"/> says why.</summary>
    Invalid,
}

/// <summary>The trip editor's answer: the editor when <see cref="Outcome"/> is <see cref="TripEditOutcome.Ok"/>.</summary>
public sealed record TripEditResult(TripEditOutcome Outcome, TripEditor? Editor = null, TripEditProblem? Problem = null);
