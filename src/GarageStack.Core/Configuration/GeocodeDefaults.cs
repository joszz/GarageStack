namespace GarageStack.Core.Configuration;

/// <summary>
/// Shared settings for reverse geocoding (turning the car's and a trip's coordinates into
/// street and city names). Nominatim's usage policy requires results to be cached rather than
/// re-requested, so the TTLs here are deliberately long: a street keeps its name for years.
/// </summary>
public static class GeocodeDefaults
{
    /// <summary>How long a resolved place stays valid before it is looked up again.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(90);

    /// <summary>
    /// How long an "upstream knows no place here" answer stays valid. Much shorter than
    /// <see cref="Ttl"/>: a blank answer can also mean the coordinate was briefly bogus, and OSM
    /// coverage grows, so it is worth asking again reasonably soon.
    /// </summary>
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(1);

    /// <summary>The languages place names can be requested in, matching the frontend's locales.</summary>
    public static readonly IReadOnlyList<string> SupportedLanguages = ["en", "nl"];

    public const string DefaultLanguage = "en";

    /// <summary>Upper bound on one batch request, so a caller cannot ask about a whole year of trips at once.</summary>
    public const int MaxPointsPerRequest = 60;

    /// <summary>
    /// Returns <paramref name="language"/> when it is one we support, otherwise the default.
    /// Requests carry it into an upstream query string, so it never leaves this allow-list.
    /// </summary>
    public static string NormalizeLanguage(string? language) =>
        language is not null && SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase)
            ? language.ToLowerInvariant()
            : DefaultLanguage;
}
