using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>The read-only API tokens, each kept as a hash with the account it reads as.</summary>
public interface IApiTokenRepository
{
    /// <summary>An account's tokens, newest first.</summary>
    Task<IReadOnlyList<ApiToken>> ListAsync(string accountKey, CancellationToken ct = default);

    Task<int> CountAsync(string accountKey, CancellationToken ct = default);

    Task<ApiToken> AddAsync(ApiToken token, CancellationToken ct = default);

    /// <returns>false when the account has no such token.</returns>
    Task<bool> DeleteAsync(string accountKey, int id, CancellationToken ct = default);

    /// <summary>The token a presented token's hash belongs to, or null for one unknown or revoked.</summary>
    Task<ApiToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    Task MarkUsedAsync(int id, DateTime usedAt, CancellationToken ct = default);
}
