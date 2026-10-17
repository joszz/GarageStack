using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Endpoints;

public static class MaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{vin}/maintenance")
            .WithTags("Maintenance")
            .RequireAuthorization()
            .AddEndpointFilter<ResolveVehicleFilter>();

        group.MapGet("/", async (
            HttpContext httpContext, IMaintenanceRepository maintenance, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            var items = await maintenance.ListAsync(vehicle.Id, ct);
            var odometerKm = await telemetry.GetOdometerAtAsync(vehicle.Id, DateTime.UtcNow, ct);

            return Results.Ok(items.Select(m => ToDto(m, odometerKm)));
        })
        .WithSummary("List maintenance items with computed due status");

        group.MapPost("/", async (
            HttpContext httpContext, CreateMaintenanceItemRequest req,
            IMaintenanceRepository maintenance, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            if ((ValidateItem(req.Name, req.Notes, req.IntervalKm, req.IntervalMonths)
                    ?? ValidateService(req.LastServiceDate, req.LastServiceOdometerKm)) is { } error)
                return ApiProblems.BadRequest(error);

            var item = await maintenance.AddAsync(new MaintenanceItem
            {
                VehicleId = vehicle.Id,
                Name = req.Name.Trim(),
                Notes = req.Notes,
                IntervalKm = req.IntervalKm,
                IntervalMonths = req.IntervalMonths,
                LastServiceDate = req.LastServiceDate,
                LastServiceOdometerKm = req.LastServiceOdometerKm,
            }, ct);

            return Results.Ok(await ToDtoAsync(item, telemetry, ct));
        })
        .WithSummary("Create a maintenance item");

        group.MapPut("/{id:int}", async (
            HttpContext httpContext, int id, UpdateMaintenanceItemRequest req,
            IMaintenanceRepository maintenance, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            if (ValidateItem(req.Name, req.Notes, req.IntervalKm, req.IntervalMonths) is { } error)
                return ApiProblems.BadRequest(error);

            var item = await maintenance.UpdateAsync(vehicle.Id, id, item =>
            {
                item.Name = req.Name.Trim();
                item.Notes = req.Notes;
                item.IntervalKm = req.IntervalKm;
                item.IntervalMonths = req.IntervalMonths;
            }, ct);
            if (item is null) return Results.NotFound();

            return Results.Ok(await ToDtoAsync(item, telemetry, ct));
        })
        .WithSummary("Update a maintenance item's name, notes and intervals");

        group.MapDelete("/{id:int}", async (
            HttpContext httpContext, int id, IMaintenanceRepository maintenance, CancellationToken ct) =>
            await maintenance.DeleteAsync(httpContext.ResolvedVehicle().Id, id, ct)
                ? Results.Ok()
                : Results.NotFound())
        .WithSummary("Delete a maintenance item and its service history");

        group.MapGet("/{id:int}/log", async (
            HttpContext httpContext, int id, IMaintenanceRepository maintenance, CancellationToken ct) =>
        {
            var entries = await maintenance.GetLogAsync(httpContext.ResolvedVehicle().Id, id, ct);
            return entries is null
                ? Results.NotFound()
                : Results.Ok(entries.Select(ToDto));
        })
        .WithSummary("List service history for a maintenance item, newest first");

        group.MapPost("/{id:int}/log", async (
            HttpContext httpContext, int id, LogMaintenanceServiceRequest req,
            IMaintenanceRepository maintenance, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            if (ValidateLogEntry(req.PerformedAt, req.OdometerKm, req.Notes) is { } error)
                return ApiProblems.BadRequest(error);

            var logged = await maintenance.LogServiceAsync(vehicle.Id, id, new MaintenanceLogEntry
            {
                PerformedAt = req.PerformedAt,
                OdometerKm = req.OdometerKm,
                Notes = req.Notes,
            }, ct);
            if (logged is null) return Results.NotFound();

            return Results.Ok(new LogMaintenanceServiceResponse(await ToDtoAsync(logged.Item, telemetry, ct), ToDto(logged.Entry)));
        })
        .WithSummary("Log a completed service, updating the item's baseline");

        group.MapDelete("/{id:int}/log/{logId:int}", async (
            HttpContext httpContext, int id, int logId,
            IMaintenanceRepository maintenance, ITelemetryRepository telemetry, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            var item = await maintenance.DeleteLogEntryAsync(vehicle.Id, id, logId, ct);
            if (item is null) return Results.NotFound();

            return Results.Ok(await ToDtoAsync(item, telemetry, ct));
        })
        .WithSummary("Remove a service log entry, recomputing the item's baseline");

        return app;
    }

    internal static ValidationError? ValidateItem(string name, string? notes, double? intervalKm, int? intervalMonths)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new("maintenance.nameRequired", "Name is required");
        if (name.Trim().Length > MaintenanceLimits.NameMaxLength)
            return new("maintenance.nameTooLong", $"Name must be {MaintenanceLimits.NameMaxLength} characters or fewer");
        if (NotesError(notes) is { } notesError)
            return notesError;
        if (intervalKm is null && intervalMonths is null)
            return new("maintenance.intervalRequired", "Set at least one of the distance or time interval");
        if (intervalKm is not null and (<= 0 or > 1_000_000))
            return new("maintenance.intervalKmOutOfRange", "Distance interval must be between 1 and 1,000,000 km");
        if (intervalMonths is not null and (<= 0 or > 120))
            return new("maintenance.intervalMonthsOutOfRange", "Time interval must be between 1 and 120 months");
        return null;
    }

    internal static ValidationError? ValidateLogEntry(DateTime performedAt, double? odometerKm, string? notes) =>
        ValidateService(performedAt, odometerKm) ?? NotesError(notes);

    /// <summary>
    /// A service as a log entry records it, or as the last service a new item starts from: not in
    /// the future, and not at a negative odometer reading.
    /// </summary>
    internal static ValidationError? ValidateService(DateTime? performedAt, double? odometerKm)
    {
        if (performedAt > DateTime.UtcNow.AddDays(1))
            return new("maintenance.serviceDateInFuture", "Service date cannot be in the future");
        if (odometerKm is < 0)
            return new("maintenance.odometerNegative", "Odometer reading cannot be negative");
        return null;
    }

    // Longer notes than the column holds would fail the insert with a 500.
    private static ValidationError? NotesError(string? notes) =>
        notes is not null && notes.Length > MaintenanceLimits.NotesMaxLength
            ? new("maintenance.notesTooLong", $"Notes must be {MaintenanceLimits.NotesMaxLength} characters or fewer")
            : null;

    private static MaintenanceLogEntryDto ToDto(MaintenanceLogEntry entry) =>
        new(entry.Id, entry.MaintenanceItemId, entry.PerformedAt, entry.OdometerKm, entry.Notes, entry.CreatedAt);

    // An item as it stands now: due against the car's current odometer reading.
    private static async Task<MaintenanceItemDto> ToDtoAsync(MaintenanceItem item, ITelemetryRepository telemetry, CancellationToken ct) =>
        ToDto(item, await telemetry.GetOdometerAtAsync(item.VehicleId, DateTime.UtcNow, ct));

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
