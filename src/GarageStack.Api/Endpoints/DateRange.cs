namespace GarageStack.Api.Endpoints;

/// <summary>The from/to window the history, statistics and trip endpoints read their data in.</summary>
internal static class DateRange
{
    /// <summary>
    /// The longest period the statistics read at once: ten years, longer than any install has
    /// recorded. Only what is summarised per day or per trip may ask for it.
    /// </summary>
    internal static readonly TimeSpan LongestStatisticsSpan = TimeSpan.FromDays(3660);

    /// <summary>
    /// Resolves the [start, end) UTC range for a from/to query: defaults the missing bound (end to
    /// now, start to end - defaultSpan) and clamps the span to maxSpan. Returns a 400 IResult if
    /// the resulting range is inverted.
    /// </summary>
    internal static IResult? TryResolve(
        DateTimeOffset? from, DateTimeOffset? to, TimeSpan defaultSpan, TimeSpan maxSpan,
        out DateTime start, out DateTime end)
    {
        end = to?.UtcDateTime ?? DateTime.UtcNow;
        start = from?.UtcDateTime ?? end - defaultSpan;

        if (start >= end)
            return ApiProblems.BadRequest("range.inverted", "from must be before to");

        if (end - start > maxSpan)
            start = end - maxSpan;

        return null;
    }
}
