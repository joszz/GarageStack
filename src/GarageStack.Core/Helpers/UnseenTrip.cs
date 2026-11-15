using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Drives no poll caught. A gateway that thinks the car is asleep polls it about once a day, so a
/// drive started without the MG cloud telling it can pass without a single fix: the car is last seen
/// standing in one place and next seen standing in another. The odometer tells such a drive from a
/// tow truck, a ferry or a GPS jump, which move the car (or its fix) without adding to the count.
/// The trip it makes is a straight line between the spots, drawn as one the car did not report, with
/// the distance the odometer counted.
/// </summary>
public static class UnseenTrip
{
    /// <summary>
    /// A spot nearer than this to where the car stood may be that same spot. Further than a parked
    /// car's fix strays: one from a garage, a multi-storey or between tall buildings has put a car
    /// standing still 700 m off its spot.
    /// </summary>
    public const double MinGapMetres = 1500;

    /// <summary>
    /// The car reported no fix for at least this long. A car being driven is polled every minute
    /// or so, so two fixes this far apart while it moved mean the polls missed the drive.
    /// </summary>
    public static readonly TimeSpan MinQuiet = TripSegmenter.ParkThreshold;

    /// <summary>
    /// And for at most this long. A sleeping car is still polled once a day, so a longer silence is
    /// the gateway being down, when any number of drives can hide in it.
    /// </summary>
    public static readonly TimeSpan MaxQuiet = TimeSpan.FromDays(2);

    /// <summary>
    /// The odometer may count up to this multiple of the straight line. Roads wind, but a drive that
    /// went much further went somewhere else first, and one straight line would hide that.
    /// </summary>
    public const double MaxOdometerRatio = 2;

    /// <summary>
    /// How fast the car is taken to have driven when nothing says when it left: an average over town
    /// and main roads, since a drive the polls missed is often a long one.
    /// </summary>
    public const double AssumedSpeedKmh = 50;

    /// <summary>
    /// Whether the car could have driven unseen from <paramref name="from"/>, where it last stood, to
    /// <paramref name="to"/>: further apart than drift, after a quiet spell no longer than a sleeping
    /// car's.
    /// </summary>
    public static bool IsCandidate(TripPoint from, TripPoint to)
    {
        var quiet = to.RecordedAt - from.RecordedAt;
        return quiet >= MinQuiet && quiet <= MaxQuiet && GapMetres(from, to) >= MinGapMetres;
    }

    /// <summary>
    /// Whether the odometer counted a drive about as far as <paramref name="straightKm"/>: at least a
    /// whole kilometre, since it reports whole kilometres, and between
    /// <see cref="TripAnchor.MinOdometerRatio"/> and <see cref="MaxOdometerRatio"/> times the straight
    /// line, a kilometre either way. A car towed or carried counts none.
    /// </summary>
    public static bool OdometerConfirms(double straightKm, double? beforeKm, double? afterKm)
    {
        if (beforeKm is not { } before || afterKm is not { } after) return false;
        var moved = after - before;
        var resolution = TripAnchor.OdometerResolutionKm;
        return moved >= Math.Max(resolution, straightKm * TripAnchor.MinOdometerRatio - resolution) &&
               moved <= straightKm * MaxOdometerRatio + resolution;
    }

    /// <summary>
    /// When the car left <paramref name="from"/> for <paramref name="first"/>, the drive's next point:
    /// when it woke up, if that is believable, otherwise <paramref name="drivenKm"/> at
    /// <see cref="AssumedSpeedKmh"/> before it. Never earlier than <paramref name="from"/>.
    /// </summary>
    /// <param name="from">Where the car stood.</param>
    /// <param name="first">The drive's next point, the first after <paramref name="from"/>.</param>
    /// <param name="wokeAt">From <see cref="TripAnchor.WakeUp"/>.</param>
    /// <param name="drivenKm">How far the odometer counted.</param>
    public static DateTime Departure(TripPoint from, TripPoint first, DateTime? wokeAt, double drivenKm)
    {
        if (wokeAt is { } woke && woke > from.RecordedAt && woke < first.RecordedAt &&
            GapMetres(from, first) / 1000 / (first.RecordedAt - woke).TotalHours <= TripAnchor.MaxWakeUpSpeedKmh)
            return woke;

        var estimate = first.RecordedAt - TimeSpan.FromHours(drivenKm / AssumedSpeedKmh);
        return estimate > from.RecordedAt ? estimate : from.RecordedAt;
    }

    /// <summary>
    /// The trip <paramref name="drive"/> was, or null when the odometer does not confirm it, or the
    /// departure would mean a speed no car drives.
    /// </summary>
    /// <param name="index">The trip's <see cref="TripDto.Index"/>.</param>
    /// <param name="drive">The candidate, from <see cref="TripSegmentation.Unseen"/>.</param>
    /// <param name="wokeAt">When the car woke up for it, from <see cref="TripAnchor.WakeUp"/>.</param>
    /// <param name="odometerBeforeKm">The odometer as the car stood at the drive's first spot.</param>
    /// <param name="odometerAfterKm">The odometer as it stood at the last.</param>
    public static TripDto? Build(int index, UnseenDrive drive, DateTime? wokeAt, double? odometerBeforeKm, double? odometerAfterKm)
    {
        var straightKm = 0.0;
        for (var i = 1; i < drive.Points.Count; i++)
            straightKm += GapMetres(drive.Points[i - 1], drive.Points[i]) / 1000;
        if (!OdometerConfirms(straightKm, odometerBeforeKm, odometerAfterKm)) return null;

        var drivenKm = odometerAfterKm!.Value - odometerBeforeKm!.Value;
        var departure = Departure(drive.Points[0], drive.Points[1], wokeAt, drivenKm);

        // A standing car's zero says nothing about how fast it drove, and would show as the trip's top speed.
        TripPoint[] points =
        [
            .. drive.Points.Select((p, i) => p with
            {
                RecordedAt = i == 0 ? departure : p.RecordedAt,
                Speed = p.Source == TripPointSource.Anchored ? null : p.Speed,
            }),
        ];
        for (var i = 1; i < points.Length; i++)
        {
            if (!TripSegmenter.IsPlausibleHop(points[i - 1], points[i])) return null;
        }

        return new TripDto(index, departure, points[^1].RecordedAt, Math.Round(drivenKm, 2), points.Length, points);
    }

    private static double GapMetres(TripPoint a, TripPoint b) =>
        GeoHelper.Haversine(a.Latitude, a.Longitude, b.Latitude, b.Longitude) * 1000;
}
