using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

public class PushSubscriptionRepository(AppDbContext db) : IPushSubscriptionRepository
{
    public async Task<IReadOnlyList<PushSubscription>> ListAsync(CancellationToken ct = default) =>
        await db.PushSubscriptions.AsNoTracking().ToListAsync(ct);

    public async Task UpsertAsync(
        string endpoint, string p256DhKey, string authKey, IReadOnlyList<string>? excludedCategories,
        CancellationToken ct = default)
    {
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);

        if (existing is not null)
        {
            // Rewritten only when something actually changed, so the common case (the same
            // subscription arriving again on the next load) costs one read and no write at all.
            var keysChanged = existing.P256DhKey != p256DhKey || existing.AuthKey != authKey;
            var exclusionsChanged = excludedCategories is not null && !excludedCategories.SequenceEqual(existing.ExcludedCategories);
            if (!keysChanged && !exclusionsChanged)
                return;

            existing.P256DhKey = p256DhKey;
            existing.AuthKey = authKey;
            if (excludedCategories is not null)
                existing.ExcludedCategories = [.. excludedCategories];
            await db.SaveChangesAsync(ct);
            return;
        }

        db.PushSubscriptions.Add(new PushSubscription
        {
            Endpoint = endpoint,
            P256DhKey = p256DhKey,
            AuthKey = authKey,
            ExcludedCategories = [.. excludedCategories ?? []],
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two clients (or two tabs) can reach the insert together, and the unique index on
            // Endpoint rejects the second one. Re-read rather than assume that is what happened:
            // if the row is there now it was that race, and the keys match anyway since a browser
            // has exactly one key pair per endpoint. If it is not, the save failed for a real
            // reason and the caller should hear about it.
            var raced = await db.PushSubscriptions.AsNoTracking().AnyAsync(s => s.Endpoint == endpoint, ct);
            if (!raced) throw;
        }
    }

    public async Task RemoveAsync(IReadOnlyCollection<string> endpoints, CancellationToken ct = default)
    {
        var gone = await db.PushSubscriptions.Where(s => endpoints.Contains(s.Endpoint)).ToListAsync(ct);
        if (gone.Count == 0) return;

        db.PushSubscriptions.RemoveRange(gone);
        await db.SaveChangesAsync(ct);
    }
}
