using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

public enum DawarichHealth { Ok, BadKey, Unreachable }

public interface IDawarichClient
{
    Task<IReadOnlyList<PhoneFix>> GetPointsAsync(DateTime from, DateTime to, CancellationToken ct);
    Task<IReadOnlyList<PhoneTracker>> GetTrackersAsync(DateTime since, CancellationToken ct);
    Task<DawarichHealth> CheckAsync(CancellationToken ct);
}

// Only safe messages cross the HTTP boundary. Inner exceptions can contain URLs or response data.
public sealed class DawarichException(string message, bool badKey = false, bool retryable = true) : Exception(message)
{
    public bool BadKey { get; } = badKey;
    public bool Retryable { get; } = retryable;
}
