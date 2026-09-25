using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;

namespace GarageStack.Worker.Services;

/// <summary>
/// Runs <see cref="TripRecorder"/> for every vehicle on a timer. A trip counts as finished five
/// minutes after the car parks, or half an hour after the last fix, so checking every few minutes
/// saves it soon after that without asking the database anything in between.
/// </summary>
public class TripRecorderService(
    ILogger<TripRecorderService> logger,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Trip recorder started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecordAllAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error while recording trips");
            }

            // Delay at the end (not the start) so a fresh install saves its history right away.
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RecordAllAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var vehicles = await scope.ServiceProvider.GetRequiredService<IVehicleRepository>().GetAllAsync(ct);
        var recorder = new TripRecorder(
            scope.ServiceProvider.GetRequiredService<ITelemetryRepository>(),
            scope.ServiceProvider.GetRequiredService<ITripRepository>(),
            logger);

        foreach (var vehicle in vehicles)
        {
            var saved = await recorder.RecordAsync(vehicle.Id, DateTime.UtcNow, ct);
            if (saved > 0)
                logger.LogInformation("Saved {Count} finished trip(s) for VIN={Vin}", saved, LogRedaction.Vin(vehicle.Vin));
        }
    }
}
