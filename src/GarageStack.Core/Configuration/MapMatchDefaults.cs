namespace GarageStack.Core.Configuration;

/// <summary>
/// Shared settings for map matching (snapping a trip's GPS fixes onto the roads OSM says they
/// were driven on). The matcher upstream is a public service, so answers are cached rather than
/// re-requested: a finished trip never changes, and the roads it ran over rarely do.
/// </summary>
public static class MapMatchDefaults
{
    /// <summary>How long a snapped trip stays valid before it is matched again.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    /// <summary>
    /// How long a "this trace could not be snapped" answer stays valid. Short, because the cause
    /// is often a stretch of road OSM has since gained, or a one-off routing quirk upstream.
    /// </summary>
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(1);

    /// <summary>
    /// Upper bound on one request. A trip with more fixes than this is thinned by the caller
    /// first: past a few hundred points the matched line gains nothing a screen can show, while
    /// the request upstream keeps growing.
    /// </summary>
    public const int MaxPointsPerRequest = 600;

    /// <summary>A trace needs two points to be a line at all.</summary>
    public const int MinPointsPerRequest = 2;

    /// <summary>
    /// Gap between consecutive fixes beyond which the trace is cut in two and each part matched
    /// on its own. Valhalla refuses a whole trace that contains a jump wider than its breakage
    /// distance (10 km on the public instance), and telemetry does produce those: a gateway that
    /// stops publishing for twenty minutes on a motorway leaves exactly that kind of hole.
    /// </summary>
    public const double MaxGapKm = 9.0;

    /// <summary>Matcher hints, in metres. Sent as-is; the public instance caps breakage at 10 km.</summary>
    public const int BreakageDistanceMeters = 9000;

    /// <summary>The car's fixes are good to roughly this, which is what tells the matcher how far it may search.</summary>
    public const int GpsAccuracyMeters = 10;

    /// <summary>How far from a fix the matcher may look for a road.</summary>
    public const int SearchRadiusMeters = 50;

    /// <summary>
    /// How much longer than the straight line through its fixes a snapped stretch may be and
    /// still be believed. How much a real route exceeds that line depends on how far apart the
    /// fixes are: consecutive fixes a hundred metres apart leave almost no room for the road to
    /// wander, while fixes a kilometre and a half apart in a city with one-way streets and canals
    /// routinely double it. So the allowance grows with the average gap, from
    /// <see cref="LengthRatioBase"/> at touching fixes by <see cref="LengthRatioPerGapKm"/> per
    /// kilometre of gap, and stops at <see cref="MaxLengthRatioCeiling"/> - past that the matcher
    /// has routed a detour rather than found the road driven, and the raw fixes are the better
    /// answer.
    /// </summary>
    public const double LengthRatioBase = 1.4;
    public const double LengthRatioPerGapKm = 0.6;
    public const double MaxLengthRatioCeiling = 2.5;

    /// <summary>
    /// How much shorter a snapped stretch may be than the line through its fixes. A match that
    /// falls well short of them has latched onto something else entirely, whatever the spacing.
    /// </summary>
    public const double MinLengthRatio = 0.6;

    /// <summary>
    /// Bumped when a change here would make cached matches differ from freshly requested ones, so
    /// that old rows are ignored instead of being served from settings nobody uses any more.
    /// Version 2 asks the matcher for speed limits as well, which rows from version 1 do not hold.
    /// </summary>
    public const int CacheVersion = 2;
}
