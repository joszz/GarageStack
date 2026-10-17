using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

public class ApiTokenRepository(AppDbContext db) : IApiTokenRepository
{
    public async Task<IReadOnlyList<ApiToken>> ListAsync(string accountKey, CancellationToken ct = default) =>
        await db.ApiTokens.AsNoTracking()
            .Where(t => t.AccountKey == accountKey)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public Task<int> CountAsync(string accountKey, CancellationToken ct = default) =>
        db.ApiTokens.CountAsync(t => t.AccountKey == accountKey, ct);

    public async Task<ApiToken> AddAsync(ApiToken token, CancellationToken ct = default)
    {
        db.ApiTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<bool> DeleteAsync(string accountKey, int id, CancellationToken ct = default)
    {
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id && t.AccountKey == accountKey, ct);
        if (token is null) return false;

        db.ApiTokens.Remove(token);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<ApiToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.ApiTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task MarkUsedAsync(int id, DateTime usedAt, CancellationToken ct = default)
    {
        // One UPDATE on PostgreSQL. The in-memory provider (demo, tests) cannot run that, so it
        // loads and saves the row instead.
        if (db.Database.IsRelational())
        {
            await db.ApiTokens.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, usedAt), ct);
            return;
        }

        var token = await db.ApiTokens.FindAsync([id], ct);
        if (token is null) return;

        token.LastUsedAt = usedAt;
        await db.SaveChangesAsync(ct);
    }
}
