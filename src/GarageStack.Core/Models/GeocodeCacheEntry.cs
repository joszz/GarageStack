namespace GarageStack.Core.Models;

/// <summary>
/// One cached reverse-geocode answer, addressed by the grid cell the queried coordinate fell
/// into (see <c>GeocodePrecisionPolicy</c>) plus the language it was asked in. A row whose
/// place fields are all null is a cached "upstream knows nothing here", which stops the same
/// empty spot being asked about again on every page load.
/// </summary>
public class GeocodeCacheEntry
{
    public int Id { get; set; }

    /// <summary>"city" or "address" - the grid resolution and upstream zoom this row was fetched at.</summary>
    public string Precision { get; set; } = string.Empty;

    public int CellLat { get; set; }
    public int CellLng { get; set; }

    /// <summary>Language the names are in; place names differ per language ("Den Haag" / "The Hague").</summary>
    public string Language { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
    public string? Road { get; set; }
    public string? HouseNumber { get; set; }
    public string? City { get; set; }
    public string? Postcode { get; set; }
    public string? CountryCode { get; set; }

    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>Column limits shared between the model configuration and the ingest code that must respect them.</summary>
public static class GeocodeCacheLimits
{
    public const int PrecisionMaxLength = 16;
    public const int LanguageMaxLength = 8;
    public const int DisplayNameMaxLength = 300;
    public const int NameMaxLength = 120;
    public const int PostcodeMaxLength = 16;
    public const int CountryCodeMaxLength = 8;
}
