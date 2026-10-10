using System.Globalization;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// The climate schedules of a vehicle. The API only keeps them; the Worker runs them when due
/// (ClimateScheduleService), from the next run computed here on every save.
/// </summary>
public static class ClimateScheduleEndpoints
{
    private const string TimeFormat = "HH:mm";

    public static IEndpointRouteBuilder MapClimateScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{vin}/climate-schedules")
            .WithTags("Climate schedules")
            .RequireAuthorization()
            .AddEndpointFilter<ResolveVehicleFilter>();

        group.MapGet("/", async (HttpContext httpContext, IClimateScheduleRepository schedules, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
            var all = await schedules.ListAsync(vehicle.Id, ct);
            return Results.Ok(all.Select(ToDto));
        })
        .WithSummary("List climate schedules, earliest time of day first");

        group.MapPost("/", async (
            HttpContext httpContext, ClimateScheduleRequest req, IClimateScheduleRepository schedules, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            var (valid, error) = Validate(req);
            if (error is not null) return ApiProblems.BadRequest(error);

            if (await schedules.CountAsync(vehicle.Id, ct) >= ClimateScheduleLimits.MaxPerVehicle)
                return ApiProblems.BadRequest("climateSchedule.limitReached",
                    $"A vehicle can have at most {ClimateScheduleLimits.MaxPerVehicle} climate schedules");

            var schedule = new ClimateSchedule { VehicleId = vehicle.Id };
            Apply(schedule, req, valid!, DateTime.UtcNow);
            await schedules.AddAsync(schedule, ct);

            return Results.Ok(ToDto(schedule));
        })
        .WithSummary("Create a climate schedule");

        group.MapPut("/{id:int}", async (
            HttpContext httpContext, int id, ClimateScheduleRequest req, IClimateScheduleRepository schedules, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            var (valid, error) = Validate(req);
            if (error is not null) return ApiProblems.BadRequest(error);

            var schedule = await schedules.UpdateAsync(vehicle.Id, id, s => Apply(s, req, valid!, DateTime.UtcNow), ct);
            return schedule is null ? Results.NotFound() : Results.Ok(ToDto(schedule));
        })
        .WithSummary("Replace a climate schedule's settings, including whether it is switched on");

        group.MapDelete("/{id:int}", async (
            HttpContext httpContext, int id, IClimateScheduleRepository schedules, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
            return await schedules.DeleteAsync(vehicle.Id, id, ct) ? Results.Ok() : Results.NotFound();
        })
        .WithSummary("Delete a climate schedule");

        return app;
    }

    /// <summary>A request's start time, days and time zone, read and checked.</summary>
    internal sealed record ValidSchedule(TimeOnly StartTime, ClimateScheduleDays Days, TimeZoneInfo Zone);

    internal static (ValidSchedule? Valid, ValidationError? Error) Validate(ClimateScheduleRequest req)
    {
        if (req.Name?.Trim().Length > ClimateScheduleLimits.NameMaxLength)
            return Refuse("nameTooLong", $"Name must be {ClimateScheduleLimits.NameMaxLength} characters or fewer");
        if (!TimeOnly.TryParseExact(req.StartTime, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return Refuse("startTimeInvalid", "Start time must be a time of day as HH:mm");
        if (ClimateScheduleDaysExtensions.FromIsoDays(req.Days ?? []) is not { } days)
            return Refuse("daysInvalid", "Days must be ISO weekday numbers from 1 (Monday) to 7 (Sunday), each once");
        if (req.TimeZoneId is not { Length: > 0 and <= ClimateScheduleLimits.TimeZoneIdMaxLength }
            || !TimeZoneInfo.TryFindSystemTimeZoneById(req.TimeZoneId, out var zone))
            return Refuse("timeZoneUnknown", "Time zone must be a known IANA time zone, such as Europe/Amsterdam");
        if (req.TemperatureC is < ClimateScheduleLimits.TemperatureMinC or > ClimateScheduleLimits.TemperatureMaxC)
            return Refuse("temperatureOutOfRange",
                $"Temperature must be between {ClimateScheduleLimits.TemperatureMinC} and {ClimateScheduleLimits.TemperatureMaxC} °C");
        if (req.SeatLeftLevel is < 0 or > ClimateScheduleLimits.SeatLevelMax
            || req.SeatRightLevel is < 0 or > ClimateScheduleLimits.SeatLevelMax)
            return Refuse("seatLevelOutOfRange", $"Seat heating levels must be between 0 and {ClimateScheduleLimits.SeatLevelMax}");
        if (OutOfRange(req.OnlyBelowC) || OutOfRange(req.OnlyAboveC))
            return Refuse("thresholdOutOfRange",
                $"Outside temperatures must be between {ClimateScheduleLimits.ThresholdMinC} and {ClimateScheduleLimits.ThresholdMaxC} °C");
        // Every temperature is either below a threshold or above one that is no higher, so the
        // condition would never skip a run.
        if (req.OnlyBelowC >= req.OnlyAboveC)
            return Refuse("conditionAlwaysTrue", "The 'colder than' temperature must be below the 'warmer than' one");

        return (new ValidSchedule(start, days, zone), null);

        static (ValidSchedule?, ValidationError?) Refuse(string code, string message) =>
            (null, new ValidationError($"climateSchedule.{code}", message));

        static bool OutOfRange(double? celsius) =>
            celsius is { } c && (!double.IsFinite(c) || c < ClimateScheduleLimits.ThresholdMinC || c > ClimateScheduleLimits.ThresholdMaxC);
    }

    // Every save works out the next run afresh: a changed time or day moves it, switching off clears it.
    private static void Apply(ClimateSchedule schedule, ClimateScheduleRequest req, ValidSchedule valid, DateTime nowUtc)
    {
        schedule.Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim();
        schedule.Enabled = req.Enabled;
        schedule.StartTime = valid.StartTime;
        schedule.Days = valid.Days;
        schedule.TimeZoneId = req.TimeZoneId;
        schedule.Mode = req.Mode;
        schedule.TemperatureC = req.TemperatureC;
        schedule.RearDefroster = req.RearDefroster;
        schedule.SeatLeftLevel = req.SeatLeftLevel;
        schedule.SeatRightLevel = req.SeatRightLevel;
        schedule.OnlyBelowC = req.OnlyBelowC;
        schedule.OnlyAboveC = req.OnlyAboveC;
        schedule.NextRunUtc = req.Enabled
            ? ClimateScheduleCalendar.NextRunUtc(valid.StartTime, valid.Days, valid.Zone, nowUtc)
            : null;
    }

    internal static ClimateScheduleDto ToDto(ClimateSchedule s) => new(
        s.Id, s.Name, s.Enabled,
        s.StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture), s.Days.ToIsoDays(), s.TimeZoneId,
        s.Mode, s.TemperatureC, s.RearDefroster, s.SeatLeftLevel, s.SeatRightLevel,
        s.OnlyBelowC, s.OnlyAboveC,
        AsUtc(s.NextRunUtc), AsUtc(s.LastRunAt), s.LastRunOutcome, s.LastRunFailedCommand, s.LastRunDetail);

    // Postgres hands timestamps back as UTC, the in-memory demo database without a kind; either
    // way they are UTC and must serialize with their Z.
    private static DateTime? AsUtc(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}

/// <summary>
/// A schedule's settings. StartTime is the time of day as HH:mm in TimeZoneId (an IANA time zone,
/// such as Europe/Amsterdam); Days are ISO weekday numbers (Monday 1 to Sunday 7), and none runs
/// the schedule once.
/// </summary>
public record ClimateScheduleRequest(
    string? Name, bool Enabled, string StartTime, int[]? Days, string TimeZoneId,
    ClimateScheduleMode Mode, int TemperatureC, bool RearDefroster, int SeatLeftLevel, int SeatRightLevel,
    double? OnlyBelowC, double? OnlyAboveC);

public record ClimateScheduleDto(
    int Id, string? Name, bool Enabled, string StartTime, int[] Days, string TimeZoneId,
    ClimateScheduleMode Mode, int TemperatureC, bool RearDefroster, int SeatLeftLevel, int SeatRightLevel,
    double? OnlyBelowC, double? OnlyAboveC,
    DateTime? NextRunUtc, DateTime? LastRunAt, ClimateScheduleOutcome? LastRunOutcome,
    string? LastRunFailedCommand, string? LastRunDetail);
