using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Worker.Services;

public sealed class PoiPreCachingService(
    ILogger<PoiPreCachingService> logger,
    IServiceScopeFactory scopeFactory,
    OverpassApiClient overpassClient,
    OcmApiClient ocmClient) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const double PreCacheRadiusKm = 100.0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("POI pre-caching service started");

        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PreCacheAllVehiclesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "POI pre-caching encountered an error");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task PreCacheAllVehiclesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IPoiRepository>();

        var vehicles = await db.Vehicles.ToListAsync(ct);

        // Single query for the latest known location of every vehicle instead of one query per
        // vehicle: the RecordedAt == MAX(RecordedAt) correlated subquery reliably translates to SQL.
        var latestByVehicle = (await db.TelemetrySnapshots
            .Where(s => s.Latitude != null && s.Longitude != null)
            .Where(s => s.RecordedAt == db.TelemetrySnapshots
                .Where(x => x.VehicleId == s.VehicleId && x.Latitude != null && x.Longitude != null)
                .Max(x => x.RecordedAt))
            .ToListAsync(ct))
            .GroupBy(s => s.VehicleId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var vehicle in vehicles)
        {
            if (!latestByVehicle.TryGetValue(vehicle.Id, out var snapshot))
                continue;
            if (snapshot.Latitude is null || snapshot.Longitude is null)
                continue;

            var lat = snapshot.Latitude.Value;
            var lng = snapshot.Longitude.Value;
            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicle);
            var vinForLog = LogRedaction.Vin(vehicle.Vin);

            foreach (var poiType in PoiTypePolicy.AllowedOverpassTypes(vehicleType))
                await PreCacheOverpassAsync(poiType, vinForLog, lat, lng, repository, ct);

            if (ocmClient.IsConfigured && VehicleTypeHelper.CanCharge(vehicleType))
                await PreCacheOcmAsync(vinForLog, lat, lng, repository, ct);
        }
    }

    private async Task PreCacheOverpassAsync(
        string poiType, string vinForLog,
        double lat, double lng,
        IPoiRepository repository,
        CancellationToken ct)
    {
        var tiles = TileHelper.ComputeTiles(lat, lng, PreCacheRadiusKm);
        var toRefresh = await repository.GetExpiredOrMissingTilesAsync(PoiCacheDefaults.OverpassSource, poiType, tiles, ct);

        if (toRefresh.Count == 0)
        {
            logger.LogDebug("All Overpass {PoiType} tiles are fresh for {Vin}", poiType, vinForLog);
            return;
        }

        logger.LogInformation("Pre-caching {Count} Overpass {PoiType} tiles for {Vin}",
            toRefresh.Count, poiType, vinForLog);

        await PoiTileFetcher.FetchAndCacheAsync(
            toRefresh,
            async (cellLat, cellLng, token) => (IReadOnlyList<PoiItem>?)await overpassClient.FetchBackgroundAsync(poiType, cellLat, cellLng, token),
            (cellLat, cellLng, items, token) => repository.UpsertTileAsync(
                PoiCacheDefaults.OverpassSource, poiType, cellLat, cellLng, items, PoiCacheDefaults.Ttl, token),
            (ex, cellLat, cellLng) => logger.LogWarning(ex,
                "Failed to pre-cache Overpass {PoiType} tile ({CellLat},{CellLng}) for {Vin}",
                poiType, cellLat, cellLng, vinForLog),
            ct);
    }

    private async Task PreCacheOcmAsync(
        string vinForLog,
        double lat, double lng,
        IPoiRepository repository,
        CancellationToken ct)
    {
        const string source = PoiCacheDefaults.OcmSource;
        const string poiType = PoiCacheDefaults.ChargingPoiType;

        var tiles = TileHelper.ComputeTiles(lat, lng, PreCacheRadiusKm);
        var toRefresh = await repository.GetExpiredOrMissingTilesAsync(source, poiType, tiles, ct);

        if (toRefresh.Count == 0)
        {
            logger.LogDebug("All OCM charging tiles are fresh for {Vin}", vinForLog);
            return;
        }

        logger.LogInformation("Pre-caching {Count} OCM charging tiles for {Vin}", toRefresh.Count, vinForLog);

        await PoiTileFetcher.FetchAndCacheAsync(
            toRefresh,
            async (cellLat, cellLng, token) => (IReadOnlyList<PoiItem>?)await ocmClient.FetchChargingStationsAsync(cellLat, cellLng, token),
            (cellLat, cellLng, items, token) => repository.UpsertTileAsync(source, poiType, cellLat, cellLng, items, PoiCacheDefaults.Ttl, token),
            (ex, cellLat, cellLng) => logger.LogWarning(ex,
                "Failed to pre-cache OCM charging tile ({CellLat},{CellLng}) for {Vin}",
                cellLat, cellLng, vinForLog),
            ct);
    }
}
