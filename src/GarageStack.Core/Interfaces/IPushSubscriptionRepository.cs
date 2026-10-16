using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>The browsers that asked for push notifications, one per push service endpoint.</summary>
public interface IPushSubscriptionRepository
{
    /// <summary>Every subscription, for sending a notification.</summary>
    Task<IReadOnlyList<PushSubscription>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores a subscription against its endpoint, inserting or updating as needed. The app
    /// registers its subscription again on every load, so this runs often and is both idempotent
    /// and safe to run twice at once. A null <paramref name="excludedCategories"/> leaves the
    /// stored list as it is.
    /// </summary>
    Task UpsertAsync(
        string endpoint, string p256DhKey, string authKey, IReadOnlyList<string>? excludedCategories,
        CancellationToken ct = default);

    /// <summary>Removes the subscriptions with these endpoints; unknown ones are ignored.</summary>
    Task RemoveAsync(IReadOnlyCollection<string> endpoints, CancellationToken ct = default);
}
