using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace GarageStack.Api.Services;

public sealed record ChargingStationDto(
    int Id,
    string Title,
    double Latitude,
    double Longitude,
    string? AddressLine,
    string? Town,
    string? Operator,
    bool? IsOperational,
    int? NumberOfPoints,
    IReadOnlyList<ConnectorDto> Connectors);

public sealed record ConnectorDto(string? Type, double? PowerKw, int? Quantity);

public sealed class ChargingStationService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<ChargingStationService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyList<ChargingStationDto>> GetStationsAsync(
        double lat, double lng, int distanceKm,
        int minPowerKw = 0, int maxPowerKw = 0,
        CancellationToken ct = default)
    {
        var apiKey = configuration["OpenChargeMap:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        // Cache key only includes minPowerKw because OCM handles it server-side.
        // maxPowerKw is applied as a post-filter so changing only the upper bound
        // reuses the cached response instead of making a new OCM request.
        var cacheKey = $"ocm:{lat:F2}:{lng:F2}:{distanceKm}:{minPowerKw}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<ChargingStationDto>? cached) && cached is not null)
            return ApplyMaxFilter(cached, maxPowerKw);

        try
        {
            var client = httpClientFactory.CreateClient("ocm");
            var url = $"https://api.openchargemap.io/v3/poi/" +
                      $"?latitude={lat}&longitude={lng}" +
                      $"&distance={distanceKm}&distanceunit=KM&maxresults=200" +
                      $"&compact=true&verbose=false" +
                      $"&statustypeid=50&client=garagestack";

            if (minPowerKw > 0)
                url += $"&minpowerkw={minPowerKw}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-API-Key", apiKey);
            var response = await client.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var raw = await response.Content.ReadFromJsonAsync<OcmPoi[]>(ct) ?? [];
            var result = (IReadOnlyList<ChargingStationDto>)raw.Select(Map).ToList();

            cache.Set(cacheKey, result, CacheTtl);
            return ApplyMaxFilter(result, maxPowerKw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch charging stations from Open Charge Map");
            return [];
        }
    }

    private static IReadOnlyList<ChargingStationDto> ApplyMaxFilter(
        IReadOnlyList<ChargingStationDto> stations, int maxPowerKw)
    {
        if (maxPowerKw <= 0) return stations;
        return stations
            .Where(s => s.Connectors.Any(c => c.PowerKw == null || c.PowerKw <= maxPowerKw))
            .ToList();
    }

    private static ChargingStationDto Map(OcmPoi p) => new(
        Id: p.AddressInfo?.Id ?? 0,
        Title: p.AddressInfo?.Title ?? string.Empty,
        Latitude: p.AddressInfo?.Latitude ?? 0,
        Longitude: p.AddressInfo?.Longitude ?? 0,
        AddressLine: p.AddressInfo?.AddressLine1,
        Town: p.AddressInfo?.Town,
        Operator: p.OperatorInfo?.Title,
        IsOperational: p.StatusType?.IsOperational,
        NumberOfPoints: p.NumberOfPoints,
        Connectors: p.Connections?.Select(c => new ConnectorDto(
            Type: c.ConnectionType?.Title,
            PowerKw: c.PowerKw,
            Quantity: c.Quantity)).ToList() ?? []);

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
