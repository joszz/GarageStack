using System.Text.Json;
using System.Text.Json.Serialization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Services;

public sealed class OverpassApiClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OverpassApiClient> logger)
{
    public const string HttpClientName = "overpass";

    // Singleton-level gate: serializes all Overpass requests so we never fire two in parallel.
    private readonly UpstreamRateGate _gate = new();

    // Background (Worker): polite interval + long 429 backoff.
    private static readonly TimeSpan BackgroundMinInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackgroundRetryAfter429 = TimeSpan.FromSeconds(180);

    // Foreground (API on-demand): moderate interval reduces burst rate; gate acquisition
    // has a hard timeout so the HTTP request never blocks the user for more than
    // ForegroundGateTimeout per tile. After a 429 a short backoff is applied so the
    // same rate-limited tile is not immediately retried on every subsequent pan.
    private static readonly TimeSpan ForegroundMinInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ForegroundGateTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ForegroundRetryAfter429 = TimeSpan.FromSeconds(30);

    private const string FuelQuery =
        "[out:json][timeout:30];(node[\"amenity\"=\"fuel\"]{bbox};way[\"amenity\"=\"fuel\"]{bbox};);out center;";
    private const string ServiceAreaQuery =
        "[out:json][timeout:30];(node[\"highway\"=\"services\"]{bbox};way[\"highway\"=\"services\"]{bbox};);out center;";
    // Cameras are mapped as nodes, including the ones an average-speed "enforcement" relation
    // ties together, so the nodes alone cover the layer and no way lookup is needed.
    private const string SpeedCameraQuery =
        "[out:json][timeout:30];node[\"highway\"=\"speed_camera\"]{bbox};out;";

    private string BaseUrl =>
        configuration["Overpass:BaseUrl"] ?? "https://overpass-api.de/api/interpreter";

    /// <summary>
    /// Whether this deployment serves <paramref name="poiType"/> at all. Only the speed camera
    /// layer can be switched off (<c>SpeedCameras:Enabled=false</c>), for the jurisdictions that
    /// restrict pointing a driver at camera positions; the other layers are always served.
    /// Both the on-demand endpoint and the Worker's pre-caching ask, so a layer that is off
    /// costs no Overpass request either.
    /// </summary>
    public bool IsTypeEnabled(string poiType) =>
        poiType != PoiTypePolicy.SpeedCamera || SpeedCamerasEnabled;

    private bool SpeedCamerasEnabled =>
        !string.Equals(configuration["SpeedCameras:Enabled"], "false", StringComparison.OrdinalIgnoreCase);

    private static string QueryFor(string poiType) => poiType switch
    {
        PoiTypePolicy.Fuel => FuelQuery,
        PoiTypePolicy.ServiceArea => ServiceAreaQuery,
        PoiTypePolicy.SpeedCamera => SpeedCameraQuery,
        _ => throw new ArgumentOutOfRangeException(nameof(poiType), poiType, "Not an Overpass POI type"),
    };

    /// <summary>
    /// Foreground (on-demand API) fetch: fails fast and returns null when Overpass is in a
    /// backoff window or the gate is held by the Worker, so the caller serves whatever is
    /// already cached. Callers must skip UpsertTileAsync on null so the tile is not cached as empty.
    /// </summary>
    public Task<IReadOnlyList<PoiItem>?> FetchAsync(string poiType, int cellLat, int cellLng, CancellationToken ct = default)
        => FetchTileAsync(poiType, cellLat, cellLng, foreground: true, ct);

    /// <summary>
    /// Background (Worker pre-caching) fetch: waits indefinitely for the gate, honours the full
    /// 429 backoff window, and throws on rate limit so the Worker can retry the tile later.
    /// </summary>
    public async Task<IReadOnlyList<PoiItem>> FetchBackgroundAsync(string poiType, int cellLat, int cellLng, CancellationToken ct = default)
        => (await FetchTileAsync(poiType, cellLat, cellLng, foreground: false, ct))!;

    private async Task<IReadOnlyList<PoiItem>?> FetchTileAsync(
        string poiType, int cellLat, int cellLng, bool foreground, CancellationToken ct)
    {
        var query = QueryFor(poiType).Replace("{bbox}", BuildBbox(cellLat, cellLng));

        if (foreground)
        {
            // Quick pre-gate check: respect the background Worker's 429 backoff window without
            // even attempting to acquire the gate.
            if (_gate.IsBackingOff)
            {
                logger.LogDebug("Overpass backoff active for {PoiType} ({CellLat},{CellLng}), serving from cache",
                    poiType, cellLat, cellLng);
                return null;
            }

            // Hard timeout on gate acquisition so the HTTP request is never held for minutes.
            if (!await _gate.WaitAsync(ForegroundGateTimeout, ct))
            {
                logger.LogDebug("Overpass gate busy for {PoiType} ({CellLat},{CellLng}), serving from cache",
                    poiType, cellLat, cellLng);
                return null;
            }
        }
        else
        {
            await _gate.WaitAsync(ct);
        }

        try
        {
            // Re-check inside the gate: the Worker may have set a backoff while we were waiting.
            if (foreground && _gate.IsBackingOff)
            {
                logger.LogDebug("Overpass backoff active (inside gate) for {PoiType} ({CellLat},{CellLng}), serving from cache",
                    poiType, cellLat, cellLng);
                return null;
            }

            await _gate.ThrottleAsync(
                foreground ? ForegroundMinInterval : BackgroundMinInterval,
                honourBackoff: !foreground,
                ct);

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]);
            _gate.MarkRequestSent();
            using var response = await client.PostAsync(BaseUrl, content, ct);

            if (UpstreamRateGate.IsThrottlingStatus((int)response.StatusCode))
            {
                if (foreground)
                {
                    // Short backoff so subsequent pans don't immediately retry the same
                    // rate-limited tile and keep getting 429 forever ("area stays blank").
                    _gate.SetBackoff(ForegroundRetryAfter429);
                    logger.LogDebug("Overpass {Status} for {PoiType} ({CellLat},{CellLng}) on foreground path, backing off {Seconds}s",
                        (int)response.StatusCode, poiType, cellLat, cellLng, (int)ForegroundRetryAfter429.TotalSeconds);
                    return null;
                }

                // Background: record backoff window so the foreground path skips Overpass for
                // the next BackgroundRetryAfter429 seconds.
                _gate.SetBackoff(BackgroundRetryAfter429);
                throw new HttpRequestException(
                    $"Overpass rate-limited ({(int)response.StatusCode}) for {poiType} ({cellLat},{cellLng})");
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync<OverpassResponse>(stream, cancellationToken: ct);
            return result?.Elements
                .Select(e => MapElement(e, poiType))
                .ToList() ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    // Tile (cellLat, cellLng) covers [cellLat/2, (cellLat+1)/2) degrees, see TileHelper.CellOf.
    private static string BuildBbox(int cellLat, int cellLng) =>
        FormattableString.Invariant($"({cellLat / 2.0},{cellLng / 2.0},{(cellLat + 1) / 2.0},{(cellLng + 1) / 2.0})");

    private static PoiItem MapElement(OverpassElement e, string poiType)
    {
        var lat = e.Type == "node" ? e.Lat : e.Center?.Lat ?? 0;
        var lng = e.Type == "node" ? e.Lon : e.Center?.Lon ?? 0;
        var meta = e.Tags is { Count: > 0 } ? JsonSerializer.Serialize(e.Tags) : null;
        var (cellLat, cellLng) = TileHelper.CellOf(lat, lng);
        return new PoiItem
        {
            Source = PoiCacheDefaults.OverpassSource,
            PoiType = poiType,
            ExternalId = $"{e.Type}/{e.Id}",
            Latitude = lat,
            Longitude = lng,
            Name = e.Tags?.GetValueOrDefault("name"),
            Brand = ExtractBrand(e.Tags),
            MetaJson = meta,
            // Tile coords derived from the element's own position, not the queried tile.
            // This prevents duplicate-key violations when the same border element appears
            // in two adjacent tile queries.
            CellLat = cellLat,
            CellLng = cellLng,
        };
    }

    // OSM tags the chain as "brand" for branded stations and "operator" for the rest; the map's
    // brand filter treats them the same way.
    private static string? ExtractBrand(Dictionary<string, string>? tags)
    {
        var brand = (tags?.GetValueOrDefault("brand") ?? tags?.GetValueOrDefault("operator"))?.Trim();
        if (string.IsNullOrEmpty(brand)) return null;
        return brand.Length <= PoiItemLimits.BrandMaxLength ? brand : brand[..PoiItemLimits.BrandMaxLength];
    }

    private sealed class OverpassResponse
    {
        [JsonPropertyName("elements")] public OverpassElement[] Elements { get; init; } = [];
    }

    private sealed class OverpassElement
    {
        [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
        [JsonPropertyName("id")] public long Id { get; init; }
        [JsonPropertyName("lat")] public double Lat { get; init; }
        [JsonPropertyName("lon")] public double Lon { get; init; }
        [JsonPropertyName("center")] public OverpassCenter? Center { get; init; }
        [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; init; }
    }

    private sealed class OverpassCenter
    {
        [JsonPropertyName("lat")] public double Lat { get; init; }
        [JsonPropertyName("lon")] public double Lon { get; init; }
    }
}
