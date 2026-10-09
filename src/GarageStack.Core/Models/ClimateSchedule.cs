using System.Text.Json.Serialization;

namespace GarageStack.Core.Models;

/// <summary>
/// A time to precondition the car: switch climate on (in a mode, at a temperature, with seat
/// heating and the rear defroster if asked) on the chosen weekdays, or once when no day is chosen.
/// The gateway has no climate schedule of its own, so the Worker sends the commands when one is due.
/// </summary>
public class ClimateSchedule
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    /// <summary>An optional label, such as "Work".</summary>
    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>The time on the clock in <see cref="TimeZoneId"/>, to the minute.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>The weekdays it repeats on, in <see cref="TimeZoneId"/>; none runs it once.</summary>
    public ClimateScheduleDays Days { get; set; }

    /// <summary>
    /// The IANA time zone the start time is read in: the browser's when the schedule was made, so
    /// 07:30 stays 07:30 across daylight saving time.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    public ClimateScheduleMode Mode { get; set; }

    /// <summary>Whole degrees Celsius; only normal climate takes a temperature.</summary>
    public int TemperatureC { get; set; } = 21;

    public bool RearDefroster { get; set; }

    /// <summary>0 (off) to 3 (high).</summary>
    public int SeatLeftLevel { get; set; }

    public int SeatRightLevel { get; set; }

    /// <summary>
    /// The outside temperature a run needs: below <see cref="OnlyBelowC"/> or above
    /// <see cref="OnlyAboveC"/>. Neither set runs it whatever the weather. Kept as typed (not
    /// rounded), so a threshold entered in Fahrenheit reads back the same.
    /// </summary>
    public double? OnlyBelowC { get; set; }

    public double? OnlyAboveC { get; set; }

    /// <summary>When it runs next, or null while it is switched off.</summary>
    public DateTime? NextRunUtc { get; set; }

    public DateTime? LastRunAt { get; set; }
    public ClimateScheduleOutcome? LastRunOutcome { get; set; }

    /// <summary>The command (e.g. seat-left) that failed or went unanswered on the last run, if any.</summary>
    public string? LastRunFailedCommand { get; set; }

    /// <summary>The gateway's reason for that failure, as it gave it.</summary>
    public string? LastRunDetail { get; set; }

    /// <summary>
    /// Bumped on every save. The Api (edits) and the Worker (claiming a run, recording how it went)
    /// both write a schedule, and this keeps either from overwriting the other unseen.
    /// </summary>
    public int Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Weekdays as flags, so a set of them is one column.</summary>
[Flags]
public enum ClimateScheduleDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
}

public static class ClimateScheduleDaysExtensions
{
    /// <summary>The flag for <paramref name="day"/>.</summary>
    public static ClimateScheduleDays Flag(this DayOfWeek day) =>
        day == DayOfWeek.Sunday ? ClimateScheduleDays.Sunday : (ClimateScheduleDays)(1 << ((int)day - 1));

    /// <summary>The days as ISO weekday numbers (Monday 1 to Sunday 7), as the API sends them.</summary>
    public static int[] ToIsoDays(this ClimateScheduleDays days) =>
        [.. Enumerable.Range(1, 7).Where(iso => days.HasFlag((ClimateScheduleDays)(1 << (iso - 1))))];

    /// <summary>The flags for ISO weekday numbers; null when one is outside 1 to 7 or repeats.</summary>
    public static ClimateScheduleDays? FromIsoDays(IReadOnlyCollection<int> isoDays)
    {
        if (isoDays.Any(d => d is < 1 or > 7) || isoDays.Distinct().Count() != isoDays.Count) return null;
        return isoDays.Aggregate(ClimateScheduleDays.None, (days, iso) => days | (ClimateScheduleDays)(1 << (iso - 1)));
    }
}

/// <summary>The climate mode a schedule starts. Each name is the value the gateway takes for it.</summary>
[JsonConverter(typeof(ClimateScheduleModeJsonConverter))]
public enum ClimateScheduleMode
{
    [JsonStringEnumMemberName("on")]
    On,

    [JsonStringEnumMemberName("blowingonly")]
    FanOnly,

    [JsonStringEnumMemberName("front")]
    FrontDefrost,
}

public static class ClimateScheduleModeExtensions
{
    /// <summary>The value climate/remoteClimateState/set takes for <paramref name="mode"/>.</summary>
    public static string ToGatewayValue(this ClimateScheduleMode mode) => mode switch
    {
        ClimateScheduleMode.FanOnly => "blowingonly",
        ClimateScheduleMode.FrontDefrost => "front",
        _ => "on",
    };
}

/// <summary>Reads and writes a mode by name only, so a request cannot store a number outside the three.</summary>
public sealed class ClimateScheduleModeJsonConverter()
    : JsonStringEnumConverter<ClimateScheduleMode>(namingPolicy: null, allowIntegerValues: false);

/// <summary>How a schedule's last run went.</summary>
[JsonConverter(typeof(ClimateScheduleOutcomeJsonConverter))]
public enum ClimateScheduleOutcome
{
    /// <summary>Sending its commands now.</summary>
    [JsonStringEnumMemberName("running")]
    Running,

    /// <summary>The car switched climate on (an extra such as seat heating may still have failed).</summary>
    [JsonStringEnumMemberName("started")]
    Started,

    /// <summary>The car refused a command, up to and including switching climate on.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,

    /// <summary>No answer came, so whether climate is on is not known.</summary>
    [JsonStringEnumMemberName("unconfirmed")]
    Unconfirmed,

    /// <summary>Not run: the car was being driven.</summary>
    [JsonStringEnumMemberName("skippedDriving")]
    SkippedDriving,

    /// <summary>Not run: the outside temperature was inside the schedule's range.</summary>
    [JsonStringEnumMemberName("skippedTemperature")]
    SkippedTemperature,

    /// <summary>Not run: GarageStack was not running when it was due.</summary>
    [JsonStringEnumMemberName("missed")]
    Missed,
}

public sealed class ClimateScheduleOutcomeJsonConverter()
    : JsonStringEnumConverter<ClimateScheduleOutcome>(namingPolicy: null, allowIntegerValues: false);

public static class ClimateScheduleLimits
{
    public const int MaxPerVehicle = 20;
    public const int NameMaxLength = 50;
    public const int TimeZoneIdMaxLength = 64;
    public const int TemperatureMinC = 16;
    public const int TemperatureMaxC = 28;
    public const int SeatLevelMax = 3;
    public const double ThresholdMinC = -40;
    public const double ThresholdMaxC = 50;
    public const int FailedCommandMaxLength = 32;

    /// <summary>The gateway's own cap on a failure's detail (GatewayCommandResult).</summary>
    public const int DetailMaxLength = 300;
}
