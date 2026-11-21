using GarageStack.Core.Interfaces;

namespace GarageStack.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", async (INotificationRepository notifications, CancellationToken ct, int limit = 100) =>
        {
            if (limit is < 1 or > 500) limit = 100;
            var list = await notifications.ListAsync(limit, ct);
            return Results.Ok(list.Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.CreatedAt, n.IsArchived, n.Category, n.VehicleId)));
        })
        .Produces<NotificationDto[]>()
        .WithSummary("List non-deleted notifications (most recent first, default limit 100)");

        group.MapPatch("/{id:int}/archive", async (int id, INotificationRepository notifications, CancellationToken ct) =>
            await notifications.ArchiveAsync(id, ct) ? Results.Ok() : Results.NotFound())
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Archive a notification");

        group.MapDelete("/{id:int}", async (int id, INotificationRepository notifications, CancellationToken ct) =>
            await notifications.DeleteAsync(id, ct) ? Results.Ok() : Results.NotFound())
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Soft-delete a notification");

        group.MapPatch("/archive-all", async (INotificationRepository notifications, CancellationToken ct) =>
        {
            await notifications.ArchiveAllAsync(ct);
            return Results.Ok();
        })
        .Produces(StatusCodes.Status200OK)
        .WithSummary("Archive all non-archived notifications");

        group.MapDelete("/", async (INotificationRepository notifications, CancellationToken ct) =>
        {
            await notifications.DeleteAllAsync(ct);
            return Results.Ok();
        })
        .Produces(StatusCodes.Status200OK)
        .WithSummary("Soft-delete all notifications");

        return app;
    }
}

// VehicleId: the car it is about; null for one about the account or the deployment.
public record NotificationDto(int Id, string Title, string Body, DateTime CreatedAt, bool IsArchived, string? Category, int? VehicleId);
