using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Services;

public sealed class OcmApiClient(
    IHttpClientFactory httpClientFactory,
    OpenChargeMapOptions options,
    ILogger<OcmApiClient> logger)
{
    public const string HttpClientName = "ocm";

    // Singleton-level gate: prevents hammering the OCM API with concurrent tile requests. Both
    // the on-demand API path and the Worker's pre-caching pass go through it, so a backoff set
    // by one is honoured by the other.
    private readonly UpstreamRateGate _gate = new();
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryAfter429 = TimeSpan.FromSeconds(60);

    private const string BaseUrl = "https://api.openchargemap.io/v3/poi/";

    public bool IsConfigured => options.IsConfigured;

    /// <returns>
    /// The tile's stations, or null when OCM was not asked because it is rate-limiting us (the
    /// tile is then left for a later pass, like Overpass does). Other failures throw, and the
    /// caller logs them.
    /// </returns>
    public async Task<IReadOnlyList<PoiItem>?> FetchChargingStationsAsync(
        int cellLat, int cellLng, CancellationToken ct = default)
    {
        var apiKey = options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        // Tile center + half-diagonal radius.
        // maxresults=5000 prevents dense cities (e.g. Amsterdam) from exhausting the limit
        // before stations in lower-density corners of the same tile (e.g. Gouda) are included.
        var centerLat = ((cellLat + 0.5) / 2.0).ToString(CultureInfo.InvariantCulture);
        var centerLng = ((cellLng + 0.5) / 2.0).ToString(CultureInfo.InvariantCulture);

        using var entry = await _gate.TryEnterAsync(waitAtMost: null, failWhenBackingOff: true, ct);

        // Skip without hitting the network while a prior request is rate-limited, so a sustained
        // limit is not hammered by every remaining tile of the Worker's pass. Not a failure: each
        // skipped tile would otherwise log a warning with a stack trace.
        if (!entry.Entered)
        {
            logger.LogDebug("OCM backing off until {Until:O}; charging tile ({CellLat},{CellLng}) left for later",
                _gate.BackoffUntil, cellLat, cellLng);
            return null;
        }

        // currenttypeid=30 = DC only; excludes AC slow chargers (Type 1/2, Schuko, etc.)
        // distance=50 covers the full 0.5°×0.5° tile diagonal at European latitudes (~33 km)
        var url = BaseUrl +
                  $"?latitude={centerLat}&longitude={centerLng}" +
                  "&distance=50&distanceunit=KM" +
                  "&maxresults=5000&compact=true&verbose=false&statustypeid=50&currenttypeid=30&client=garagestack";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-API-Key", apiKey);

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var answer = await _gate.SendAsync(
            token => client.SendAsync(request, token), MinInterval, honourBackoff: false, RetryAfter429, ct);
        var response = answer.Response;

        if (answer.Backoff is { } backoff)
        {
            logger.LogWarning("OCM rate-limited ({Status}); pausing charging station requests for {Seconds}s",
                (int)response.StatusCode, (int)backoff.TotalSeconds);
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var pois = await JsonSerializer.DeserializeAsync<OcmPoi[]>(stream, cancellationToken: ct) ?? [];
        return pois.Select(MapToPoiItem).ToList();
    }

    // OCM connection type IDs for common DC connectors (from OCM reference data).
    // Used as fallback when compact=true responses omit the Title string.
    private static readonly Dictionary<int, string> ConnectorTypeNames = new()
    {
        { 2,  "CHAdeMO" },
        { 25, "CCS Type 2" },
        { 32, "CCS Type 1" },
        { 27, "Tesla Supercharger" },
        { 30, "Tesla (Roadster)" },
        { 33, "NACS / Tesla" },
    };

    private static string ResolveConnectorType(OcmConnection c)
    {
        if (!string.IsNullOrWhiteSpace(c.ConnectionType?.Title))
            return c.ConnectionType.Title;
        var typeId = c.ConnectionType?.Id ?? c.ConnectionTypeId;
        if (typeId is int id && ConnectorTypeNames.TryGetValue(id, out var name))
            return name;
        return "Unknown";
    }

    private static PoiItem MapToPoiItem(OcmPoi p)
    {
        var lat = p.AddressInfo?.Latitude ?? 0;
        var lng = p.AddressInfo?.Longitude ?? 0;
        var (cellLat, cellLng) = TileHelper.CellOf(lat, lng);

        // Only store DC connectors (CurrentTypeID == 30); AC already excluded at API level
        // but guard here too in case OCM returns mixed results.
        var connectors = p.Connections?
            .Where(c => c.PowerKw.HasValue && c.CurrentTypeId is null or 30)
            .GroupBy(c => (type: ResolveConnectorType(c), powerKw: (int)Math.Round(c.PowerKw!.Value)))
            .Select(g => new OcmConnectorMeta(g.Key.type, g.Key.powerKw, g.Sum(c => c.Quantity ?? 1)))
            .ToArray() ?? [];

        var meta = new OcmMeta(
            p.AddressInfo?.AddressLine1,
            p.AddressInfo?.Town,
            p.OperatorInfo?.Title,
            p.StatusType?.IsOperational,
            p.NumberOfPoints,
            connectors);

        var operatorName = p.OperatorInfo?.Title?.Trim();

        return new PoiItem
        {
            Source = PoiCacheDefaults.OcmSource,
            PoiType = PoiCacheDefaults.ChargingPoiType,
            ExternalId = $"ocm/{p.AddressInfo?.Id ?? 0}",
            Latitude = lat,
            Longitude = lng,
            Name = p.AddressInfo?.Title,
            Brand = string.IsNullOrEmpty(operatorName)
                ? null
                : operatorName.Truncate(PoiItemLimits.BrandMaxLength),
            MetaJson = JsonSerializer.Serialize(meta),
            // Tile from element's own position (same pattern as Overpass) so that a station
            // returned by an adjacent tile's radius query doesn't collide on the unique index.
            CellLat = cellLat,
            CellLng = cellLng,
        };
    }

    // ---------------------------------------------------------------------------
    // Internal DTO stored as MetaJson -- used by ChargingStationService to
    // reconstruct ChargingStationDto without re-fetching from OCM.
    // ---------------------------------------------------------------------------

    public sealed record OcmMeta(
        [property: JsonPropertyName("addressLine")] string? AddressLine,
        [property: JsonPropertyName("town")] string? Town,
        [property: JsonPropertyName("operator")] string? Operator,
        [property: JsonPropertyName("isOperational")] bool? IsOperational,
        [property: JsonPropertyName("numberOfPoints")] int? NumberOfPoints,
        [property: JsonPropertyName("connectors")] OcmConnectorMeta[] Connectors);

    public sealed record OcmConnectorMeta(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("powerKw")] int PowerKw,
        [property: JsonPropertyName("quantity")] int Quantity);

    // ---------------------------------------------------------------------------
    // Private OCM response models
    // ---------------------------------------------------------------------------

    private sealed class OcmPoi
    {
        [JsonPropertyName("AddressInfo")] public OcmAddressInfo? AddressInfo { get; init; }
        [JsonPropertyName("Connections")] public OcmConnection[]? Connections { get; init; }
        [JsonPropertyName("OperatorInfo")] public OcmOperatorInfo? OperatorInfo { get; init; }
        [JsonPropertyName("StatusType")] public OcmStatusType? StatusType { get; init; }
        [JsonPropertyName("NumberOfPoints")] public int? NumberOfPoints { get; init; }
    }

    private sealed class OcmAddressInfo
    {
        [JsonPropertyName("ID")] public int Id { get; init; }
        [JsonPropertyName("Title")] public string? Title { get; init; }
        [JsonPropertyName("AddressLine1")] public string? AddressLine1 { get; init; }
        [JsonPropertyName("Town")] public string? Town { get; init; }
        [JsonPropertyName("Latitude")] public double Latitude { get; init; }
        [JsonPropertyName("Longitude")] public double Longitude { get; init; }
    }

    private sealed class OcmConnection
    {
        // In compact=true mode OCM returns a flat integer instead of a nested object.
        // Capture both forms so ResolveConnectorType can fall back to the flat ID.
        [JsonPropertyName("ConnectionType")] public OcmConnectionType? ConnectionType { get; init; }
        [JsonPropertyName("ConnectionTypeID")] public int? ConnectionTypeId { get; init; }
        [JsonPropertyName("CurrentTypeID")] public int? CurrentTypeId { get; init; }
        [JsonPropertyName("PowerKW")] public double? PowerKw { get; init; }
        [JsonPropertyName("Quantity")] public int? Quantity { get; init; }
    }

    private sealed class OcmConnectionType
    {
        [JsonPropertyName("ID")] public int? Id { get; init; }
        [JsonPropertyName("Title")] public string? Title { get; init; }
    }

    private sealed class OcmOperatorInfo
    {
        [JsonPropertyName("Title")] public string? Title { get; init; }
    }

    private sealed class OcmStatusType
    {
        [JsonPropertyName("IsOperational")] public bool? IsOperational { get; init; }
    }
}
