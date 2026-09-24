using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// Caches reverse-geocode answers (coordinate to street/city) so the same spot is never asked
/// about twice: Nominatim's usage policy requires exactly that. Entries are addressed by the
/// integer <c>(CellLat, CellLng)</c> grid cell for a <c>precision</c> plus the
/// <c>language</c> they were fetched in, never by raw coordinates.
/// </summary>
public interface IGeocodeRepository
{
    /// <summary>
    /// Returns the still-valid cached entries among <paramref name="cells"/>. Expired and
    /// missing cells are simply absent from the result, so a caller treats both the same way.
    /// </summary>
    Task<IReadOnlyList<GeocodeCacheEntry>> GetValidAsync(
        string precision, string language,
        IReadOnlyList<(int CellLat, int CellLng)> cells,
        CancellationToken ct = default);

    /// <summary>Stores (or refreshes) one cell's answer, valid for <paramref name="ttl"/>.</summary>
    Task UpsertAsync(
        string precision, string language,
        int cellLat, int cellLng,
        PlaceAddress place,
        TimeSpan ttl,
        CancellationToken ct = default);
}
