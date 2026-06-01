using GarageStack.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var notifications = await db.AppNotifications
                .AsNoTracking()
                .Where(n => !n.IsDeleted)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.CreatedAt, n.IsArchived, n.Category))
                .ToListAsync(ct);
            return Results.Ok(notifications);
        })
        .WithSummary("List all non-deleted notifications");

        group.MapPatch("/{id:int}/archive", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var notification = await db.AppNotifications.FindAsync([id], ct);
            if (notification is null || notification.IsDeleted) return Results.NotFound();
            notification.IsArchived = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        })
        .WithSummary("Archive a notification");

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var notification = await db.AppNotifications.FindAsync([id], ct);
            if (notification is null) return Results.NotFound();
            notification.IsDeleted = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        })
        .WithSummary("Soft-delete a notification");

        return app;
    }
}

public record NotificationDto(int Id, string Title, string Body, DateTime CreatedAt, bool IsArchived, string? Category);
