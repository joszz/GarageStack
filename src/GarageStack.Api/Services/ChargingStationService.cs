using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Services;
using Microsoft.Extensions.Logging;

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
    IPoiRepository repository,
    OcmApiClient ocmClient,
    ILogger<ChargingStationService> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<ChargingStationDto>> GetStationsAsync(
        double lat, double lng, int distanceKm,
        int minPowerKw = 0, int maxPowerKw = 0,
        CancellationToken ct = default)
    {
        if (!ocmClient.IsConfigured) return [];

        var tiles = TileHelper.ComputeTiles(lat, lng, distanceKm);
        var uncached = await repository.GetUncachedTilesAsync("ocm", "charging", tiles, ct);

        // Cap on-demand fetches to the 3 tiles closest to the viewport centre so the API
        // never takes 80+ seconds on a cold cache. The Worker fills remaining tiles in the background.
        const int MaxOnDemandTiles = 1;
        var centerCellLat = (int)Math.Floor(lat * 2);
        var centerCellLng = (int)Math.Floor(lng * 2);
        var toFetch = uncached
            .OrderBy(t => Math.Abs(t.CellLat - centerCellLat) + Math.Abs(t.CellLng - centerCellLng))
            .Take(MaxOnDemandTiles)
            .ToList();

        foreach (var (cellLat, cellLng) in toFetch)
        {
            try
            {
                var items = await ocmClient.FetchChargingStationsAsync(cellLat, cellLng, ct);
                await repository.UpsertTileAsync("ocm", "charging", cellLat, cellLng, items, Ttl, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "On-demand OCM fetch failed for charging ({CellLat},{CellLng})",
                    cellLat, cellLng);
            }
        }

        var lngFactor = 111.0 * Math.Cos(lat * Math.PI / 180.0);
        var minLat = lat - distanceKm / 111.0;
        var maxLat = lat + distanceKm / 111.0;
        var minLng = lngFactor > 0 ? lng - distanceKm / lngFactor : lng - 1.0;
        var maxLng = lngFactor > 0 ? lng + distanceKm / lngFactor : lng + 1.0;

        var pois = await repository.GetPoisInBoundsAsync("ocm", "charging", minLat, minLng, maxLat, maxLng, ct);
        var stations = pois.Select(MapToDto).ToList();

        return ApplyPowerFilter(stations, minPowerKw, maxPowerKw);
    }

    private static ChargingStationDto MapToDto(PoiItem p)
    {
        OcmApiClient.OcmMeta? meta = null;
        if (p.MetaJson is not null)
        {
            try { meta = JsonSerializer.Deserialize<OcmApiClient.OcmMeta>(p.MetaJson); }
            catch { }
        }

        var connectors = meta?.Connectors
            .Select(c => new ConnectorDto(c.Type, c.PowerKw, c.Quantity))
            .ToList() ?? (IReadOnlyList<ConnectorDto>)[];

        // Reconstruct the OCM integer ID from ExternalId ("ocm/12345")
        var ocmId = 0;
        if (p.ExternalId.StartsWith("ocm/", StringComparison.Ordinal))
            int.TryParse(p.ExternalId[4..], out ocmId);

        return new ChargingStationDto(
            ocmId,
            p.Name ?? string.Empty,
            p.Latitude,
            p.Longitude,
            meta?.AddressLine,
            meta?.Town,
            meta?.Operator,
            meta?.IsOperational,
            meta?.NumberOfPoints,
            connectors);
    }

    private static IReadOnlyList<ChargingStationDto> ApplyPowerFilter(
        List<ChargingStationDto> stations, int minPowerKw, int maxPowerKw)
    {
        IEnumerable<ChargingStationDto> filtered = stations;
        if (minPowerKw > 0)
            filtered = filtered.Where(s => s.Connectors.Any(c => c.PowerKw >= minPowerKw));
        if (maxPowerKw > 0)
            filtered = filtered.Where(s => s.Connectors.Any(c => c.PowerKw == null || c.PowerKw <= maxPowerKw));
        return filtered.ToList();
    }
}
