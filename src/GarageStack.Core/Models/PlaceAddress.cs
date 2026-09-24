namespace GarageStack.Core.Models;

/// <summary>
/// A place as reverse geocoding knows it: the parts a caller may want to compose into a label,
/// never a pre-formatted string, so the frontend decides how an address reads per locale.
/// <c>City</c> is the most specific settlement name (city, town, village or hamlet) and
/// <c>DisplayName</c> the upstream's own full label, a fallback for when the parts are thin.
/// </summary>
public sealed record PlaceAddress(
    string? DisplayName,
    string? Road,
    string? HouseNumber,
    string? City,
    string? Postcode,
    string? CountryCode)
{
    /// <summary>Upstream answered, but knows no place at that coordinate (mid-sea, unmapped desert).</summary>
    public static readonly PlaceAddress Empty = new(null, null, null, null, null, null);

    public bool IsEmpty =>
        DisplayName is null && Road is null && HouseNumber is null
        && City is null && Postcode is null && CountryCode is null;
}
