using GarageStack.Api.Endpoints;
using GarageStack.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GarageStack.Api.Services;

/// <summary>
/// Whether the gateway is signed in to the MG account, as the Worker's heartbeat last said (or the
/// demo panel), and the browsers to tell when that changes. Without it the app could only say the
/// car is offline, while the car is fine and it is the gateway that gets no data at all.
/// </summary>
public sealed class GatewaySignInMonitor(IHubContext<TelemetryHub> hub, ILogger<GatewaySignInMonitor> logger)
{
    /// <summary>The SignalR event a change goes out on, with a <see cref="GatewayStatus"/>.</summary>
    public const string ChangedEvent = "gatewayStatus";

    private readonly Lock _lock = new();
    private bool? _signedIn;

    public bool? SignedIn
    {
        get
        {
            lock (_lock) return _signedIn;
        }
    }

    public GatewayStatus Status => new(SignedIn);

    /// <summary>Records what the gateway says now. A change goes out to every browser; the same answer again does not.</summary>
    public async Task UpdateAsync(bool? signedIn, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_signedIn == signedIn) return;
            _signedIn = signedIn;
        }

        try
        {
            await hub.Clients.All.SendAsync(ChangedEvent, new GatewayStatus(signedIn), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to broadcast the gateway sign-in status");
        }
    }
}
