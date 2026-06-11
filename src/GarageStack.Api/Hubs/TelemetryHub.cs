using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GarageStack.Api.Hubs;

[Authorize]
public class TelemetryHub : Hub
{
    public Task JoinVehicle(int vehicleId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"vehicle-{vehicleId}");

    public Task LeaveVehicle(int vehicleId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"vehicle-{vehicleId}");
}
