using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GarageStack.Api.Hubs;

[Authorize]
public class TelemetryHub : Hub
{
    /// <summary>The group a browser joins to hear about one vehicle, and the API sends that vehicle's updates to.</summary>
    public static string GroupFor(int vehicleId) => $"vehicle-{vehicleId}";

    public Task JoinVehicle(int vehicleId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(vehicleId));

    public Task LeaveVehicle(int vehicleId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(vehicleId));
}
