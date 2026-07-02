using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
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

            await PoiTileFetcher.FetchAndCacheAsync(
                toRefresh,
                async (cellLat, cellLng, token) =>
                {
                    IReadOnlyList<PoiItem> items = poiType switch
                    {
                        "fuel" => await overpassClient.FetchFuelStationsBackgroundAsync(cellLat, cellLng, token),
                        "service_area" => await overpassClient.FetchServiceAreasBackgroundAsync(cellLat, cellLng, token),
                        _ => [],
                    };
                    return (IReadOnlyList<PoiItem>?)items;
                },
                (cellLat, cellLng, items, token) => repository.UpsertTileAsync("overpass", poiType, cellLat, cellLng, items, Ttl, token),
                (ex, cellLat, cellLng) => logger.LogWarning(ex,
                    "Failed to pre-cache Overpass {PoiType} tile ({CellLat},{CellLng}) for {Vin}",
                    poiType, cellLat, cellLng, vin),
                ct);
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

        await PoiTileFetcher.FetchAndCacheAsync(
            toRefresh,
            async (cellLat, cellLng, token) => (IReadOnlyList<PoiItem>?)await ocmClient.FetchChargingStationsAsync(cellLat, cellLng, token),
            (cellLat, cellLng, items, token) => repository.UpsertTileAsync("ocm", "charging", cellLat, cellLng, items, Ttl, token),
            (ex, cellLat, cellLng) => logger.LogWarning(ex,
                "Failed to pre-cache OCM charging tile ({CellLat},{CellLng}) for {Vin}",
                cellLat, cellLng, vin),
            ct);
    }
}
