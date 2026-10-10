using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Services;

/// <summary>
/// Sends the commands the Worker asks for on the vehicle_command_requested channel (a climate
/// schedule that is due). Only the Api publishes commands, so they pass the same checks and the same
/// one-at-a-time gate as a command from the browser. The Worker waits for the gateway's answer
/// itself, so nothing is sent back: a request that fails here simply goes unanswered.
/// </summary>
internal sealed class VehicleCommandRequestHandler(
    IServiceScopeFactory scopeFactory,
    VehicleCommandSender sender,
    ILogger<VehicleCommandRequestHandler> logger,
    TimeSpan? deadline = null)
{
    /// <summary>
    /// How long a request may wait for the gate, behind commands from a browser, before it is
    /// dropped. Well inside the Worker's wait for the answer, so a command is never published after
    /// the Worker has given up on it.
    /// </summary>
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(90);

    private readonly TimeSpan _deadline = deadline ?? DefaultDeadline;

    /// <summary>Never throws: it runs detached from the LISTEN loop.</summary>
    internal async Task HandleAsync(string json, CancellationToken stoppingToken)
    {
        VehicleCommandRequestPayload? request = null;
        try
        {
            request = VehicleCommandRequestPayload.FromJson(json);
            if (request is null) return;

            using var expiry = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            expiry.CancelAfter(_deadline);

            using var scope = scopeFactory.CreateScope();
            var vehicles = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();
            var vehicle = await vehicles.GetByVinAsync(request.Vin, expiry.Token);
            if (vehicle is null)
            {
                logger.LogWarning("Requested command {Command} ({RequestId}) is for an unknown VIN={Vin}; not sent",
                    request.Command, request.RequestId, LogRedaction.Vin(request.Vin));
                return;
            }

            var outcome = await sender.SendAsync(vehicle, request.Command, request.Value, expiry.Token);
            if (outcome.Refusal is { } refusal)
                logger.LogWarning("Requested command {Command} ({RequestId}) refused: {Reason}",
                    request.Command, request.RequestId, refusal.Message);
            else
                logger.LogInformation("Sent requested command {Command} ({RequestId}) for vehicleId={VehicleId}",
                    request.Command, request.RequestId, vehicle.Id);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Requested command {Command} ({RequestId}) waited {Seconds}s for the car's previous command; not sent",
                request?.Command, request?.RequestId, _deadline.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send requested command {Command} ({RequestId})", request?.Command, request?.RequestId);
        }
    }
}
