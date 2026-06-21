using System.Text.Json;
using System.Text.Json.Serialization;
using GarageStack.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Services;

public sealed class OverpassApiClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OverpassApiClient> logger)
{
    // Singleton-level gate: serializes all Overpass requests so we never fire two in parallel.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    // Written inside the gate (background path on 429), read outside it (foreground pre-gate
    // check). Interlocked ensures cross-thread visibility without a full lock.
    private long _backoffUntilTicks = DateTimeOffset.MinValue.UtcTicks;

    // Background (Worker): polite interval + long 429 backoff.
    private static readonly TimeSpan BackgroundMinInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackgroundRetryAfter429 = TimeSpan.FromSeconds(180);

    // Foreground (API on-demand): moderate interval reduces burst rate; gate acquisition
    // has a hard timeout so the HTTP request never blocks the user for more than
    // ForegroundGateTimeout per tile. After a 429 a short backoff is applied so the
    // same rate-limited tile is not immediately retried on every subsequent pan.
    private static readonly TimeSpan ForegroundMinInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ForegroundGateTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ForegroundRetryAfter429 = TimeSpan.FromSeconds(60);

    private string BaseUrl =>
        configuration["Overpass:BaseUrl"] ?? "https://overpass-api.de/api/interpreter";

    // ── Foreground (on-demand API path) ──────────────────────────────────────────────────────
    // Fail fast: return [] if in a 429 backoff window or the gate is held by the Worker.
    // The API caller serves whatever is already cached.

    // Returns null when Overpass is temporarily unavailable (backoff / gate busy).
    // Callers must skip UpsertTileAsync on null so the tile is not cached as empty.
    public Task<IReadOnlyList<PoiItem>?> FetchFuelStationsAsync(int cellLat, int cellLng, CancellationToken ct = default)
        => FetchTileAsync("overpass", "fuel", cellLat, cellLng,
            "[out:json][timeout:30];(node[\"amenity\"=\"fuel\"]{bbox};way[\"amenity\"=\"fuel\"]{bbox};);out center;",
            foreground: true, ct);

    public Task<IReadOnlyList<PoiItem>?> FetchServiceAreasAsync(int cellLat, int cellLng, CancellationToken ct = default)
        => FetchTileAsync("overpass", "service_area", cellLat, cellLng,
            "[out:json][timeout:30];(node[\"highway\"=\"services\"]{bbox};way[\"highway\"=\"services\"]{bbox};);out center;",
            foreground: true, ct);

    // ── Background (Worker pre-caching path) ─────────────────────────────────────────────────
    // Wait indefinitely for the gate and honour the full 429 backoff window.
    // Background path never returns null -- it throws on rate-limit so the Worker can retry.

    public async Task<IReadOnlyList<PoiItem>> FetchFuelStationsBackgroundAsync(int cellLat, int cellLng, CancellationToken ct = default)
        => (await FetchTileAsync("overpass", "fuel", cellLat, cellLng,
            "[out:json][timeout:30];(node[\"amenity\"=\"fuel\"]{bbox};way[\"amenity\"=\"fuel\"]{bbox};);out center;",
            foreground: false, ct))!;

    public async Task<IReadOnlyList<PoiItem>> FetchServiceAreasBackgroundAsync(int cellLat, int cellLng, CancellationToken ct = default)
        => (await FetchTileAsync("overpass", "service_area", cellLat, cellLng,
            "[out:json][timeout:30];(node[\"highway\"=\"services\"]{bbox};way[\"highway\"=\"services\"]{bbox};);out center;",
            foreground: false, ct))!;

    // ── Core implementation ───────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<PoiItem>?> FetchTileAsync(
        string source, string poiType,
        int cellLat, int cellLng,
        string queryTemplate,
        bool foreground,
        CancellationToken ct)
    {
        var minLat = cellLat / 2.0;
        var minLng = cellLng / 2.0;
        var maxLat = (cellLat + 1) / 2.0;
        var maxLng = (cellLng + 1) / 2.0;
        var bbox = $"({minLat.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                   $"{minLng.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                   $"{maxLat.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                   $"{maxLng.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
        var query = queryTemplate.Replace("{bbox}", bbox);

        if (foreground)
        {
            // Quick pre-gate check: respect the background Worker's 429 backoff window without
            // even attempting to acquire the gate.
            if (new DateTimeOffset(Interlocked.Read(ref _backoffUntilTicks), TimeSpan.Zero) > DateTimeOffset.UtcNow)
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
            if (foreground)
            {
                // Re-check inside the gate: the Worker may have set _backoffUntilTicks while we
                // were waiting for _gate.WaitAsync to return.
                if (new DateTimeOffset(Interlocked.Read(ref _backoffUntilTicks), TimeSpan.Zero) > DateTimeOffset.UtcNow)
                {
                    logger.LogDebug("Overpass backoff active (inside gate) for {PoiType} ({CellLat},{CellLng}), serving from cache",
                        poiType, cellLat, cellLng);
                    return null;
                }
            }

            // Background: nextAllowed = max(_lastRequestAt + BackgroundMinInterval, _backoffUntil)
            // Foreground: nextAllowed = _lastRequestAt + ForegroundMinInterval
            DateTimeOffset nextAllowed;
            if (!foreground)
            {
                var backoffUntil = new DateTimeOffset(Interlocked.Read(ref _backoffUntilTicks), TimeSpan.Zero);
                var fromLastRequest = _lastRequestAt + BackgroundMinInterval;
                nextAllowed = backoffUntil > fromLastRequest ? backoffUntil : fromLastRequest;
            }
            else
            {
                nextAllowed = _lastRequestAt + ForegroundMinInterval;
            }

            var wait = nextAllowed - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            var client = httpClientFactory.CreateClient("overpass");
            using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]);
            _lastRequestAt = DateTimeOffset.UtcNow;
            var response = await client.PostAsync(BaseUrl, content, ct);

            if ((int)response.StatusCode is 429 or 503 or 504)
            {
                if (foreground)
                {
                    // Set a short backoff so subsequent pans don't immediately retry the same
                    // rate-limited tile and keep getting 429 forever ("area stays blank").
                    Interlocked.Exchange(ref _backoffUntilTicks,
                        (DateTimeOffset.UtcNow + ForegroundRetryAfter429).UtcTicks);
                    logger.LogDebug("Overpass {Status} for {PoiType} ({CellLat},{CellLng}) on foreground path, backing off {Seconds}s",
                        (int)response.StatusCode, poiType, cellLat, cellLng, (int)ForegroundRetryAfter429.TotalSeconds);
                    return null;
                }

                // Background: record backoff window so the foreground path skips Overpass for
                // the next BackgroundRetryAfter429 seconds.
                Interlocked.Exchange(ref _backoffUntilTicks,
                    (DateTimeOffset.UtcNow + BackgroundRetryAfter429).UtcTicks);
                throw new HttpRequestException(
                    $"Overpass rate-limited ({(int)response.StatusCode}) for {poiType} ({cellLat},{cellLng})");
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync<OverpassResponse>(stream, cancellationToken: ct);
            return result?.Elements
                .Select(e => MapElement(e, source, poiType))
                .ToList() ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    private static PoiItem MapElement(OverpassElement e, string source, string poiType)
    {
        var lat = e.Type == "node" ? e.Lat : e.Center?.Lat ?? 0;
        var lng = e.Type == "node" ? e.Lon : e.Center?.Lon ?? 0;
        var meta = e.Tags is { Count: > 0 } ? JsonSerializer.Serialize(e.Tags) : null;
        return new PoiItem
        {
            Source = source,
            PoiType = poiType,
            ExternalId = $"{e.Type}/{e.Id}",
            Latitude = lat,
            Longitude = lng,
            Name = e.Tags?.GetValueOrDefault("name"),
            MetaJson = meta,
            // Tile coords derived from the element's own position, not the queried tile.
            // This prevents duplicate-key violations when the same border element appears
            // in two adjacent tile queries.
            CellLat = (int)Math.Floor(lat * 2),
            CellLng = (int)Math.Floor(lng * 2),
        };
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
