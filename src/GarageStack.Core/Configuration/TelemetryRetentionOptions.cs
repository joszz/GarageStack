using Microsoft.Extensions.Configuration;

namespace GarageStack.Core.Configuration;

/// <summary>
/// How long telemetry keeps every row before the Worker compacts it into quarter-hour rows
/// (TELEMETRY_FULL_DETAIL_DAYS). 0 or less switches compaction off. A setting below
/// <see cref="MinimumDays"/> is raised to it, since the map reads that far back at full detail. The
/// statistics read further, but only per day, and compaction keeps each day's figures.
/// </summary>
public sealed class TelemetryRetentionOptions
{
    public const int DefaultDays = 365;

    /// <summary>The longest period the map asks the API for.</summary>
    public const int MinimumDays = 90;

    /// <summary>Days of telemetry kept at full detail, or null when compaction is off.</summary>
    public int? FullDetailDays { get; init; } = DefaultDays;

    public static TelemetryRetentionOptions From(IConfiguration configuration)
    {
        var days = configuration.IntegerOrDefault("TelemetryRetention:FullDetailDays", DefaultDays);
        return new() { FullDetailDays = days <= 0 ? null : Math.Max(days, MinimumDays) };
    }
}
