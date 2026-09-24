using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GarageStack.Data.Repositories;

// logger is optional (DI always supplies one) so tests can construct this with just a DbContext.
public class GeocodeRepository(AppDbContext db, ILogger<GeocodeRepository>? logger = null) : IGeocodeRepository
{
    public async Task<IReadOnlyList<GeocodeCacheEntry>> GetValidAsync(
        string precision, string language,
        IReadOnlyList<(int CellLat, int CellLng)> cells,
        CancellationToken ct = default)
    {
        if (cells.Count == 0) return [];

        var now = DateTime.UtcNow;
        var cellLats = cells.Select(c => c.CellLat).Distinct().ToList();
        var cellLngs = cells.Select(c => c.CellLng).Distinct().ToList();

        // Two IN lists narrow the query to the index; they also match cells at the corners of the
        // cross product nobody asked about, so the exact set is filtered out of the (small) result.
        var candidates = await db.GeocodeCacheEntries
            .AsNoTracking()
            .Where(e => e.Precision == precision && e.Language == language
                        && e.ExpiresAt > now
                        && cellLats.Contains(e.CellLat)
                        && cellLngs.Contains(e.CellLng))
            .ToListAsync(ct);

        var requested = cells.ToHashSet();
        return candidates.Where(e => requested.Contains((e.CellLat, e.CellLng))).ToList();
    }

    public async Task UpsertAsync(
        string precision, string language,
        int cellLat, int cellLng,
        PlaceAddress place,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        try
        {
            await UpsertAttemptAsync(precision, language, cellLat, cellLng, place, ttl, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Another request asked about the same cell between our read and our insert. Retry
            // once: the second attempt finds the now-committed row and updates it instead.
            logger?.LogDebug("Geocode cache row for {Precision} was inserted concurrently, retrying as an update", precision);
            db.ChangeTracker.Clear();
            await UpsertAttemptAsync(precision, language, cellLat, cellLng, place, ttl, ct);
        }
    }

    private async Task UpsertAttemptAsync(
        string precision, string language,
        int cellLat, int cellLng,
        PlaceAddress place,
        TimeSpan ttl,
        CancellationToken ct)
    {
        var entry = await db.GeocodeCacheEntries.FirstOrDefaultAsync(
            e => e.Precision == precision && e.Language == language
                 && e.CellLat == cellLat && e.CellLng == cellLng, ct);

        if (entry is null)
        {
            entry = new GeocodeCacheEntry
            {
                Precision = precision,
                Language = language,
                CellLat = cellLat,
                CellLng = cellLng,
            };
            db.GeocodeCacheEntries.Add(entry);
        }

        var now = DateTime.UtcNow;
        entry.DisplayName = place.DisplayName;
        entry.Road = place.Road;
        entry.HouseNumber = place.HouseNumber;
        entry.City = place.City;
        entry.Postcode = place.Postcode;
        entry.CountryCode = place.CountryCode;
        entry.CachedAt = now;
        entry.ExpiresAt = now.Add(ttl);

        await db.SaveChangesAsync(ct);
    }
}
