using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Api.Endpoints;

public static class MaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{vin}/maintenance")
            .WithTags("Maintenance")
            .RequireAuthorization()
            .AddEndpointFilter<VehicleEndpoints.ResolveVehicleFilter>();

        group.MapGet("/", async (HttpContext httpContext, AppDbContext db, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = VehicleEndpoints.ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var items = await db.MaintenanceItems.AsNoTracking()
                .Where(m => m.VehicleId == vehicle.Id)
                .OrderBy(m => m.Name)
                .ToListAsync(ct);

            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);

            return Results.Ok(items.Select(m => ToDto(m, snapshot?.OdometerKm)));
        })
        .WithSummary("List maintenance items with computed due status");

        group.MapPost("/", async (
            HttpContext httpContext, CreateMaintenanceItemRequest req,
            AppDbContext db, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = VehicleEndpoints.ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var error = ValidateItem(req.Name, req.Notes, req.IntervalKm, req.IntervalMonths);
            if (error is not null) return Results.BadRequest(new { error });

            var item = new MaintenanceItem
            {
                VehicleId = vehicle.Id,
                Name = req.Name.Trim(),
                Notes = req.Notes,
                IntervalKm = req.IntervalKm,
                IntervalMonths = req.IntervalMonths,
                LastServiceDate = req.LastServiceDate,
                LastServiceOdometerKm = req.LastServiceOdometerKm,
            };

            db.MaintenanceItems.Add(item);
            await db.SaveChangesAsync(ct);

            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            return Results.Ok(ToDto(item, snapshot?.OdometerKm));
        })
        .WithSummary("Create a maintenance item");

        group.MapPut("/{id:int}", async (
            HttpContext httpContext, int id, UpdateMaintenanceItemRequest req,
            AppDbContext db, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = VehicleEndpoints.ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var item = await db.MaintenanceItems.FirstOrDefaultAsync(m => m.Id == id && m.VehicleId == vehicle.Id, ct);
            if (item is null) return Results.NotFound();

            var error = ValidateItem(req.Name, req.Notes, req.IntervalKm, req.IntervalMonths);
            if (error is not null) return Results.BadRequest(new { error });

            item.Name = req.Name.Trim();
            item.Notes = req.Notes;
            item.IntervalKm = req.IntervalKm;
            item.IntervalMonths = req.IntervalMonths;

            await db.SaveChangesAsync(ct);

            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            return Results.Ok(ToDto(item, snapshot?.OdometerKm));
        })
        .WithSummary("Update a maintenance item's name, notes and intervals");

        group.MapDelete("/{id:int}", async (HttpContext httpContext, int id, AppDbContext db, CancellationToken ct) =>
        {
            var vehicle = VehicleEndpoints.ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var item = await db.MaintenanceItems.FirstOrDefaultAsync(m => m.Id == id && m.VehicleId == vehicle.Id, ct);
            if (item is null) return Results.NotFound();

            db.MaintenanceItems.Remove(item);
            await db.SaveChangesAsync(ct);

            return Results.Ok();
        })
        .WithSummary("Delete a maintenance item and its service history");

        group.MapGet("/{id:int}/log", async (HttpContext httpContext, int id, AppDbContext db, CancellationToken ct) =>
        {
            var vehicle = VehicleEndpoints.ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var itemExists = await db.MaintenanceItems.AnyAsync(m => m.Id == id && m.VehicleId == vehicle.Id, ct);
            if (!itemExists) return Results.NotFound();

            var entries = await db.MaintenanceLogEntries.AsNoTracking()
                .Where(l => l.MaintenanceItemId == id)
                .OrderByDescending(l => l.PerformedAt)
                .Select(l => new MaintenanceLogEntryDto(l.Id, l.MaintenanceItemId, l.PerformedAt, l.OdometerKm, l.Notes, l.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(entries);
        })
        .WithSummary("List service history for a maintenance item, newest first");

        group.MapPost("/{id:int}/log", async (
            HttpContext httpContext, int id, LogMaintenanceServiceRequest req,
            AppDbContext db, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = VehicleEndpoints.ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var item = await db.MaintenanceItems.FirstOrDefaultAsync(m => m.Id == id && m.VehicleId == vehicle.Id, ct);
            if (item is null) return Results.NotFound();

            var error = ValidateLogEntry(req.PerformedAt, req.OdometerKm);
            if (error is not null) return Results.BadRequest(new { error });

            var entry = new MaintenanceLogEntry
            {
                MaintenanceItemId = id,
                PerformedAt = req.PerformedAt,
                OdometerKm = req.OdometerKm,
                Notes = req.Notes,
            };
            db.MaintenanceLogEntries.Add(entry);
            await db.SaveChangesAsync(ct);

            await RecomputeBaselineAsync(item, db, ct);
            await db.SaveChangesAsync(ct);

            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            var logDto = new MaintenanceLogEntryDto(entry.Id, entry.MaintenanceItemId, entry.PerformedAt, entry.OdometerKm, entry.Notes, entry.CreatedAt);

            return Results.Ok(new LogMaintenanceServiceResponse(ToDto(item, snapshot?.OdometerKm), logDto));
        })
        .WithSummary("Log a completed service, updating the item's baseline");

        group.MapDelete("/{id:int}/log/{logId:int}", async (
            HttpContext httpContext, int id, int logId,
            AppDbContext db, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = VehicleEndpoints.ResolveVehicleFilter.GetResolvedVehicle(httpContext);

            var item = await db.MaintenanceItems.FirstOrDefaultAsync(m => m.Id == id && m.VehicleId == vehicle.Id, ct);
            if (item is null) return Results.NotFound();

            var entry = await db.MaintenanceLogEntries.FirstOrDefaultAsync(l => l.Id == logId && l.MaintenanceItemId == id, ct);
            if (entry is null) return Results.NotFound();

            db.MaintenanceLogEntries.Remove(entry);
            await db.SaveChangesAsync(ct);

            await RecomputeBaselineAsync(item, db, ct);
            await db.SaveChangesAsync(ct);

            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            return Results.Ok(ToDto(item, snapshot?.OdometerKm));
        })
        .WithSummary("Remove a service log entry, recomputing the item's baseline");

        return app;
    }

    // Baseline always reflects the entry with the latest PerformedAt, not the most recently
    // inserted one, so back-dating an older service after a newer one already exists doesn't
    // corrupt the due-status calculation.
    private static async Task RecomputeBaselineAsync(MaintenanceItem item, AppDbContext db, CancellationToken ct)
    {
        var latest = await db.MaintenanceLogEntries.AsNoTracking()
            .Where(l => l.MaintenanceItemId == item.Id)
            .OrderByDescending(l => l.PerformedAt)
            .FirstOrDefaultAsync(ct);

        item.LastServiceDate = latest?.PerformedAt;
        item.LastServiceOdometerKm = latest?.OdometerKm;
    }

    internal static string? ValidateItem(string name, string? notes, double? intervalKm, int? intervalMonths)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Name is required";
        if (name.Trim().Length > 200) return "Name must be 200 characters or fewer";
        if (notes is not null && notes.Length > 1000) return "Notes must be 1000 characters or fewer";
        if (intervalKm is null && intervalMonths is null) return "Set at least one of the distance or time interval";
        if (intervalKm is not null and (<= 0 or > 1_000_000)) return "Distance interval must be between 1 and 1,000,000 km";
        if (intervalMonths is not null and (<= 0 or > 120)) return "Time interval must be between 1 and 120 months";
        return null;
    }

    internal static string? ValidateLogEntry(DateTime performedAt, double? odometerKm)
    {
        if (performedAt > DateTime.UtcNow.AddDays(1)) return "Service date cannot be in the future";
        if (odometerKm is < 0) return "Odometer reading cannot be negative";
        return null;
    }

    private static MaintenanceItemDto ToDto(MaintenanceItem item, double? currentOdometerKm)
    {
        var result = MaintenanceDueCalculator.Calculate(
            item.IntervalKm, item.IntervalMonths,
            item.LastServiceDate, item.LastServiceOdometerKm,
            currentOdometerKm, DateTime.UtcNow);

        var status = result.Status switch
        {
            MaintenanceDueStatus.Ok => "ok",
            MaintenanceDueStatus.DueSoon => "dueSoon",
            MaintenanceDueStatus.Overdue => "overdue",
            _ => "unknown",
        };

        return new MaintenanceItemDto(
            item.Id, item.VehicleId, item.Name, item.Notes,
            item.IntervalKm, item.IntervalMonths,
            item.LastServiceDate, item.LastServiceOdometerKm,
            status, result.NextDueDate, result.NextDueOdometerKm,
            result.DaysRemaining, result.KmRemaining, item.CreatedAt);
    }
}

public record MaintenanceItemDto(
    int Id, int VehicleId, string Name, string? Notes,
    double? IntervalKm, int? IntervalMonths,
    DateTime? LastServiceDate, double? LastServiceOdometerKm,
    string DueStatus, DateTime? NextDueDate, double? NextDueOdometerKm,
    int? DaysRemaining, double? KmRemaining, DateTime CreatedAt);

public record CreateMaintenanceItemRequest(
    string Name, string? Notes, double? IntervalKm, int? IntervalMonths,
    DateTime? LastServiceDate, double? LastServiceOdometerKm);

public record UpdateMaintenanceItemRequest(string Name, string? Notes, double? IntervalKm, int? IntervalMonths);

public record LogMaintenanceServiceRequest(DateTime PerformedAt, double? OdometerKm, string? Notes);

public record MaintenanceLogEntryDto(int Id, int MaintenanceItemId, DateTime PerformedAt, double? OdometerKm, string? Notes, DateTime CreatedAt);

public record LogMaintenanceServiceResponse(MaintenanceItemDto Item, MaintenanceLogEntryDto LogEntry);
