using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Services;

/// <summary>
/// Reverse geocoding against Nominatim (OpenStreetMap's own geocoder, or a self-hosted instance
/// via <c>Geocoding:BaseUrl</c>). The public instance allows at most one request per second and
/// expects answers to be cached, which is why every caller goes through
/// <see cref="GarageStack.Core.Interfaces.IGeocodeRepository"/> first and only reaches this
/// client for a coordinate nothing has asked about yet.
/// </summary>
public sealed class NominatimApiClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<NominatimApiClient> logger)
{
    public const string HttpClientName = "nominatim";

    // Singleton-level gate: one request in flight at a time, at most one per MinInterval.
    private readonly UpstreamRateGate _gate = new();

    // The public instance's ceiling is one request per second; the margin keeps a clock that
    // rounds the wrong way from turning into a 429.
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1100);

    // A caller is a browser waiting on a map or a trip list, so it never queues behind more than
    // a couple of other lookups: past that the request returns what it has and the client asks
    // again for the rest.
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RetryAfter429 = TimeSpan.FromSeconds(60);

    private const string DefaultBaseUrl = "https://nominatim.openstreetmap.org";

    /// <summary>
    /// Deployments that would rather not send coordinates to a third party can set
    /// <c>Geocoding:Enabled=false</c> (and keep place names by pointing BaseUrl at their own
    /// Nominatim). Anything other than an explicit "false" leaves geocoding on.
    /// </summary>
    public bool IsEnabled =>
        !string.Equals(configuration["Geocoding:Enabled"], "false", StringComparison.OrdinalIgnoreCase);

    private string BaseUrl =>
        (configuration["Geocoding:BaseUrl"] ?? DefaultBaseUrl).TrimEnd('/');

    /// <summary>
    /// Looks up one coordinate. Returns <see cref="PlaceAddress.Empty"/> when upstream answered
    /// but knows no place there (a cacheable answer), and null when we could not ask at all -
    /// the gate was held, a backoff window was open, or the request failed - so the caller
    /// leaves the coordinate unresolved rather than caching a blank.
    /// </summary>
    public async Task<PlaceAddress?> ReverseAsync(
        double lat, double lng, string precision, string language, CancellationToken ct = default)
    {
        if (!IsEnabled) return null;

        // Pre-gate check: skip the queue entirely while a backoff window is open.
        if (_gate.IsBackingOff)
        {
            logger.LogDebug("Nominatim backoff active, leaving {Precision} lookup unresolved", precision);
            return null;
        }

        if (!await _gate.WaitAsync(GateTimeout, ct))
        {
            logger.LogDebug("Nominatim gate busy, leaving {Precision} lookup unresolved", precision);
            return null;
        }

        try
        {
            // Re-check inside the gate: the request we queued behind may have been rate-limited.
            if (_gate.IsBackingOff)
            {
                logger.LogDebug("Nominatim backoff active (inside gate), leaving {Precision} lookup unresolved", precision);
                return null;
            }

            await _gate.ThrottleAsync(MinInterval, honourBackoff: true, ct);

            var url = BuildReverseUrl(lat, lng, precision, language);
            var client = httpClientFactory.CreateClient(HttpClientName);
            _gate.MarkRequestSent();
            using var response = await client.GetAsync(url, ct);

            if (UpstreamRateGate.IsThrottlingStatus((int)response.StatusCode))
            {
                _gate.SetBackoff(RetryAfter429);
                logger.LogDebug("Nominatim {Status}, backing off {Seconds}s",
                    (int)response.StatusCode, (int)RetryAfter429.TotalSeconds);
                return null;
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync<NominatimReverseResponse>(stream, cancellationToken: ct);
            return result is null ? PlaceAddress.Empty : MapResponse(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Coordinates stay out of the log: a failing geocoder is worth knowing about, the
            // car's position is not worth writing to disk on every retry.
            logger.LogWarning(ex, "Nominatim reverse lookup failed for a {Precision} coordinate", precision);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Every value here is either a number formatted invariantly or, for the language, one of the
    // two codes GeocodeDefaults allows, so nothing needs escaping into the query string.
    private string BuildReverseUrl(double lat, double lng, string precision, string language)
    {
        var invariant = CultureInfo.InvariantCulture;
        var zoom = GeocodePrecisionPolicy.ZoomFor(precision).ToString(invariant);
        return $"{BaseUrl}/reverse?format=jsonv2&lat={lat.ToString("G9", invariant)}" +
               $"&lon={lng.ToString("G9", invariant)}" +
               $"&zoom={zoom}&addressdetails=1&accept-language={language}";
    }

    // OSM tags the settlement under whichever of these fits its size, so the first one present
    // is the name a driver would recognise; the administrative fallbacks keep rural coordinates
    // from coming back nameless.
    private static readonly string[] CityKeys =
    [
        "city", "town", "village", "hamlet", "municipality", "suburb", "city_district", "county",
    ];

    private static PlaceAddress MapResponse(NominatimReverseResponse response)
    {
        // Nominatim answers an unmappable coordinate with {"error": ...} and no address, which is
        // a real answer worth caching, not a failure.
        if (response.Error is not null) return PlaceAddress.Empty;

        var address = response.Address;
        return new PlaceAddress(
            Clean(response.DisplayName, GeocodeCacheLimits.DisplayNameMaxLength),
            Clean(Tag(address, "road"), GeocodeCacheLimits.NameMaxLength),
            Clean(Tag(address, "house_number"), GeocodeCacheLimits.NameMaxLength),
            Clean(CityKeys.Select(k => Tag(address, k)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
                GeocodeCacheLimits.NameMaxLength),
            Clean(Tag(address, "postcode"), GeocodeCacheLimits.PostcodeMaxLength),
            Clean(Tag(address, "country_code"), GeocodeCacheLimits.CountryCodeMaxLength));
    }

    private static string? Tag(Dictionary<string, string>? address, string key) =>
        address?.GetValueOrDefault(key);

    private static string? Clean(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed class NominatimReverseResponse
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
        [JsonPropertyName("address")] public Dictionary<string, string>? Address { get; init; }

        // Typed as an element because the field is a plain string on some instances and an
        // object on others; we only ever check whether it is there at all.
        [JsonPropertyName("error")] public JsonElement? Error { get; init; }
    }
}
