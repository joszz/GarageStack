using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Data;
using GarageStack.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GarageStack.Worker.Services;

public sealed class PoiPreCachingService(
    ILogger<PoiPreCachingService> logger,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
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
        var overpassClient = scope.ServiceProvider.GetRequiredService<OverpassApiClient>();
        var ocmClient = scope.ServiceProvider.GetRequiredService<OcmApiClient>();

        var vehicles = await db.Vehicles.ToListAsync(ct);

        foreach (var vehicle in vehicles)
        {
            var snapshot = await db.TelemetrySnapshots
                .Where(s => s.VehicleId == vehicle.Id
                            && s.Latitude != null && s.Longitude != null)
                .OrderByDescending(s => s.RecordedAt)
                .FirstOrDefaultAsync(ct);

            if (snapshot?.Latitude is null || snapshot.Longitude is null)
                continue;

            var lat = snapshot.Latitude.Value;
            var lng = snapshot.Longitude.Value;
            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicle);

            await PreCacheOverpassAsync(vehicleType, vehicle.Vin, lat, lng, repository, overpassClient, ct);

            if (ocmClient.IsConfigured)
                await PreCacheOcmAsync(vehicleType, vehicle.Vin, lat, lng, repository, ocmClient, ct);
        }
    }

    private async Task PreCacheOverpassAsync(
        string vehicleType, string vin,
        double lat, double lng,
        IPoiRepository repository, OverpassApiClient overpassClient,
        CancellationToken ct)
    {
        var poiTypes = vehicleType switch
        {
            "hev" or "phev" => new[] { "fuel", "service_area" },
            _ => ["service_area"],
        };

        foreach (var poiType in poiTypes)
        {
            var tiles = TileHelper.ComputeTiles(lat, lng, PreCacheRadiusKm);
            var toRefresh = await repository.GetExpiredOrMissingTilesAsync("overpass", poiType, tiles, ct);

            if (toRefresh.Count == 0)
            {
                logger.LogDebug("All Overpass {PoiType} tiles are fresh for {Vin}", poiType, vin);
                continue;
            }

            logger.LogInformation("Pre-caching {Count} Overpass {PoiType} tiles for {Vin}",
                toRefresh.Count, poiType, vin);

            foreach (var (cellLat, cellLng) in toRefresh)
            {
                try
                {
                    var items = poiType switch
                    {
                        "fuel" => await overpassClient.FetchFuelStationsBackgroundAsync(cellLat, cellLng, ct),
                        "service_area" => await overpassClient.FetchServiceAreasBackgroundAsync(cellLat, cellLng, ct),
                        _ => [],
                    };
                    await repository.UpsertTileAsync("overpass", poiType, cellLat, cellLng, items, Ttl, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex,
                        "Failed to pre-cache Overpass {PoiType} tile ({CellLat},{CellLng}) for {Vin}",
                        poiType, cellLat, cellLng, vin);
                }
            }
        }
    }

    private async Task PreCacheOcmAsync(
        string vehicleType, string vin,
        double lat, double lng,
        IPoiRepository repository, OcmApiClient ocmClient,
        CancellationToken ct)
    {
        if (vehicleType is not ("bev" or "phev"))
            return;

        var tiles = TileHelper.ComputeTiles(lat, lng, PreCacheRadiusKm);
        var toRefresh = await repository.GetExpiredOrMissingTilesAsync("ocm", "charging", tiles, ct);

        if (toRefresh.Count == 0)
        {
            logger.LogDebug("All OCM charging tiles are fresh for {Vin}", vin);
            return;
        }

        logger.LogInformation("Pre-caching {Count} OCM charging tiles for {Vin}", toRefresh.Count, vin);

        foreach (var (cellLat, cellLng) in toRefresh)
        {
            try
            {
                var items = await ocmClient.FetchChargingStationsAsync(cellLat, cellLng, ct);
                await repository.UpsertTileAsync("ocm", "charging", cellLat, cellLng, items, Ttl, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "Failed to pre-cache OCM charging tile ({CellLat},{CellLng}) for {Vin}",
                    cellLat, cellLng, vin);
            }
        }
    }
}
