using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GarageStack.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Services;

public sealed class OcmApiClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OcmApiClient> logger)
{
    // Singleton-level gate: prevents hammering the OCM API with concurrent tile requests
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(500);

    private const string BaseUrl = "https://api.openchargemap.io/v3/poi/";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["OpenChargeMap:ApiKey"]);

    public async Task<IReadOnlyList<PoiItem>> FetchChargingStationsAsync(
        int cellLat, int cellLng, CancellationToken ct = default)
    {
        var apiKey = configuration["OpenChargeMap:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return [];

        // Tile center + half-diagonal radius.
        // maxresults=5000 prevents dense cities (e.g. Amsterdam) from exhausting the limit
        // before stations in lower-density corners of the same tile (e.g. Gouda) are included.
        var centerLat = ((cellLat + 0.5) / 2.0).ToString(CultureInfo.InvariantCulture);
        var centerLng = ((cellLng + 0.5) / 2.0).ToString(CultureInfo.InvariantCulture);

        await _gate.WaitAsync(ct);
        try
        {
            var wait = MinInterval - (DateTimeOffset.UtcNow - _lastRequestAt);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

            // currenttypeid=30 = DC only; excludes AC slow chargers (Type 1/2, Schuko, etc.)
            // distance=50 covers the full 0.5°×0.5° tile diagonal at European latitudes (~33 km)
            var url = BaseUrl +
                      $"?latitude={centerLat}&longitude={centerLng}" +
                      "&distance=50&distanceunit=KM" +
                      "&maxresults=5000&compact=true&verbose=false&statustypeid=50&currenttypeid=30&client=garagestack";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-API-Key", apiKey);

            var client = httpClientFactory.CreateClient("ocm");
            _lastRequestAt = DateTimeOffset.UtcNow;
            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var pois = await JsonSerializer.DeserializeAsync<OcmPoi[]>(stream, cancellationToken: ct) ?? [];
            return pois.Select(MapToPoiItem).ToList();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "OCM fetch failed for charging tile ({CellLat},{CellLng})", cellLat, cellLng);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static PoiItem MapToPoiItem(OcmPoi p)
    {
        var lat = p.AddressInfo?.Latitude ?? 0;
        var lng = p.AddressInfo?.Longitude ?? 0;

        // Only store DC connectors (CurrentTypeID == 30); AC already excluded at API level
        // but guard here too in case OCM returns mixed results.
        var connectors = p.Connections?
            .Where(c => c.PowerKw.HasValue && c.CurrentTypeId is null or 30)
            .GroupBy(c => (type: c.ConnectionType?.Title ?? "Unknown", powerKw: (int)Math.Round(c.PowerKw!.Value)))
            .Select(g => new OcmConnectorMeta(g.Key.type, g.Key.powerKw, g.Sum(c => c.Quantity ?? 1)))
            .ToArray() ?? [];

        var meta = new OcmMeta(
            p.AddressInfo?.AddressLine1,
            p.AddressInfo?.Town,
            p.OperatorInfo?.Title,
            p.StatusType?.IsOperational,
            p.NumberOfPoints,
            connectors);

        return new PoiItem
        {
            Source = "ocm",
            PoiType = "charging",
            ExternalId = $"ocm/{p.AddressInfo?.Id ?? 0}",
            Latitude = lat,
            Longitude = lng,
            Name = p.AddressInfo?.Title,
            MetaJson = JsonSerializer.Serialize(meta),
            // Tile from element's own position (same pattern as Overpass) so that a station
            // returned by an adjacent tile's radius query doesn't collide on the unique index.
            CellLat = (int)Math.Floor(lat * 2),
            CellLng = (int)Math.Floor(lng * 2),
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
        [JsonPropertyName("ConnectionType")] public OcmConnectionType? ConnectionType { get; init; }
        [JsonPropertyName("CurrentTypeID")] public int? CurrentTypeId { get; init; }
        [JsonPropertyName("PowerKW")] public double? PowerKw { get; init; }
        [JsonPropertyName("Quantity")] public int? Quantity { get; init; }
    }

    private sealed class OcmConnectionType
    {
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
