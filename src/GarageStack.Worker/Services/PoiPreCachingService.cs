using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Services;

namespace GarageStack.Worker.Services;

public sealed class PoiPreCachingService(
    ILogger<PoiPreCachingService> logger,
    IServiceScopeFactory scopeFactory,
    OverpassApiClient overpassClient,
    OcmApiClient ocmClient) : PeriodicBackgroundService(logger)
{
    private const double PreCacheRadiusKm = 100.0;

    protected override string Name => "POI pre-caching";

    protected override TimeSpan InitialDelay => TimeSpan.FromMinutes(2);

    protected override TimeSpan Interval => TimeSpan.FromHours(6);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPoiRepository>();
        var vehicleRepo = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();

        var vehicles = await vehicleRepo.GetAllAsync(ct);

        foreach (var vehicle in vehicles)
        {
            // One short index read per car (IX_TelemetrySnapshots_VehicleId_RecordedAt_Gps).
            if (await telemetry.GetLatestPositionAsync(vehicle.Id, ct) is not { } position)
                continue;

            var lat = position.Latitude;
            var lng = position.Longitude;
            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicle);
            var vinForLog = LogRedaction.Vin(vehicle.Vin);

            // Two questions, both of which have to say yes: the car has a use for the layer, and
            // the deployment serves it (speed cameras can be switched off).
            foreach (var poiType in PoiTypePolicy.AllowedOverpassTypes(vehicleType).Where(overpassClient.IsTypeEnabled))
                await PreCacheAsync(
                    repository, PoiCacheDefaults.OverpassSource, poiType, "Overpass", vinForLog, lat, lng,
                    async (cellLat, cellLng, token) => await overpassClient.FetchBackgroundAsync(poiType, cellLat, cellLng, token),
                    ct);

            if (ocmClient.IsConfigured && VehicleTypeHelper.CanCharge(vehicleType))
                await PreCacheAsync(
                    repository, PoiCacheDefaults.OcmSource, PoiCacheDefaults.ChargingPoiType, "OCM", vinForLog, lat, lng,
                    ocmClient.FetchChargingStationsAsync,
                    ct);
        }
    }

    private async Task PreCacheAsync(
        IPoiRepository repository,
        string source, string poiType, string upstream, string vinForLog,
        double lat, double lng,
        Func<int, int, CancellationToken, Task<IReadOnlyList<PoiItem>?>> fetch,
        CancellationToken ct)
    {
        var refresh = await PoiTileFetcher.RefreshAsync(
            repository, source, poiType, lat, lng, PreCacheRadiusKm, maxTiles: null, fetch,
            (ex, cellLat, cellLng) => logger.LogWarning(ex,
                "Failed to pre-cache {Upstream} {PoiType} tile ({CellLat},{CellLng}) for {Vin}",
                upstream, poiType, cellLat, cellLng, vinForLog),
            ct);

        if (refresh.Missing == 0)
            logger.LogDebug("All {Upstream} {PoiType} tiles are fresh for {Vin}", upstream, poiType, vinForLog);
        else
            logger.LogInformation("Pre-cached {Cached} of {Missing} {Upstream} {PoiType} tiles for {Vin}",
                refresh.Cached, refresh.Missing, upstream, poiType, vinForLog);
    }
}
