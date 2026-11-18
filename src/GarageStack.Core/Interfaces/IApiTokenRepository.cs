using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>The read-only API tokens, each kept as a hash with the account it reads as.</summary>
public interface IApiTokenRepository
{
    /// <summary>An account's tokens of one scope, newest first.</summary>
    Task<IReadOnlyList<ApiToken>> ListAsync(string accountKey, ApiTokenScope scope, CancellationToken ct = default);

    Task<int> CountAsync(string accountKey, CancellationToken ct = default);

    Task<ApiToken> AddAsync(ApiToken token, CancellationToken ct = default);

    /// <summary>
    /// Adds <paramref name="token"/> in place of every token its account has of the same scope, in
    /// one save: for a scope an account has one of, such as the calendar feed's.
    /// </summary>
    Task<ApiToken> ReplaceAsync(ApiToken token, CancellationToken ct = default);

    /// <returns>false when the account has no such token.</returns>
    Task<bool> DeleteAsync(string accountKey, int id, CancellationToken ct = default);

    /// <returns>false when the account had no token of that scope.</returns>
    Task<bool> DeleteScopeAsync(string accountKey, ApiTokenScope scope, CancellationToken ct = default);

    /// <summary>The token a presented token's hash belongs to, or null for one unknown or revoked.</summary>
    Task<ApiToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    Task MarkUsedAsync(int id, DateTime usedAt, CancellationToken ct = default);
}
