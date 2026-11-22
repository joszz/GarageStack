using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

public sealed record PhoneFusionWork(
    long TripId, int VehicleId, string PointsJson, IReadOnlyList<TripPoint> Points,
    TripPoint? Parked, DateTime? PreviousEnd, IReadOnlyList<LinkedPhoneTracker> Trackers);

/// <summary>Linked tracker identifiers only. No credentials or phone positions.</summary>
public sealed record LinkedPhoneTracker(string TrackerId, DateTimeOffset CreatedAt);

public interface IPhoneFusionRepository
{
    Task<IReadOnlyList<LinkedPhoneTracker>> ListAsync(int vehicleId, CancellationToken ct);
    Task SetAsync(int vehicleId, IReadOnlyList<string> trackers, DateTime now, CancellationToken ct);
    Task RequestBackfillAsync(int vehicleId, DateTime from, DateTime until, CancellationToken ct);
    Task QueueBackfillAsync(CancellationToken ct);
    /// <summary>
    /// Trips ready to fuse: ended before <paramref name="before"/>, with a linked Dawarich tracker
    /// (only when <paramref name="dawarich"/>, Dawarich being configured) or positions from the app.
    /// </summary>
    Task<IReadOnlyList<long>> PendingAsync(DateTime before, int limit, bool dawarich, CancellationToken ct);
    /// <summary>What fusing the trip needs; its Dawarich trackers only when <paramref name="dawarich"/>.</summary>
    Task<PhoneFusionWork?> ReadWorkAsync(long id, bool dawarich, CancellationToken ct);
    Task<PhoneFusionOutcome?> CompleteAsync(PhoneFusionWork work, PhoneFusionResult result, DateTime now, CancellationToken ct);
}
