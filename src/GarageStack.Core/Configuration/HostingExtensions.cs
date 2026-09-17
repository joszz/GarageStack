using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace GarageStack.Core.Configuration;

/// <summary>
/// Startup wiring the API and the Worker share. Both are hosts over the same deployment: they
/// read the same environment, log the same way (console plus a rolling file, with DEBUG_LOGS
/// opening the taps) and colour tyre pressure by the same thresholds.
/// </summary>
public static class HostingExtensions
{
    private const int RetainedLogFileCount = 30;

    /// <summary>
    /// Logger for the window before the host exists, so a failure while reading configuration
    /// still reaches the console instead of vanishing. Replaced by the configured logger as soon
    /// as the host is built.
    /// </summary>
    public static ILogger CreateBootstrapLogger() =>
        new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

    /// <summary>
    /// Logging as both services do it: console plus a daily rolling file kept for a month, with
    /// levels read from configuration and DEBUG_LOGS=true turning on debug output. The
    /// <c>logFilePrefix</c> names that file, so "api" writes logs/api-20260916.log.
    /// </summary>
    public static IServiceCollection AddGarageStackSerilog(
        this IServiceCollection services, IConfiguration configuration, string logFilePrefix)
    {
        var debugLogs = string.Equals(configuration["DEBUG_LOGS"], "true", StringComparison.OrdinalIgnoreCase);

        return services.AddSerilog((_, config) =>
        {
            config.ReadFrom.Configuration(configuration)
                  .WriteTo.Console()
                  .WriteTo.File(
                      $"logs/{logFilePrefix}-.log",
                      rollingInterval: RollingInterval.Day,
                      retainedFileCountLimit: RetainedLogFileCount);

            if (debugLogs)
                config.MinimumLevel.Debug()
                      .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                      .MinimumLevel.Override("System", LogEventLevel.Warning);
        });
    }

    /// <summary>
    /// Tyre pressure thresholds from configuration, falling back per value to the generic
    /// passenger-car defaults. The API colour-codes the dashboard with them and the Worker decides
    /// whether a pressure is worth a notification, so both need the same numbers.
    /// </summary>
    public static IServiceCollection AddTyrePressureThresholds(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("TyrePressure");
        var defaults = TyrePressureThresholds.Default;

        return services.AddSingleton(new TyrePressureThresholds(
            Value(section, "LowBar", defaults.LowBar),
            Value(section, "GoodBar", defaults.GoodBar),
            Value(section, "HighBar", defaults.HighBar)));

        static double Value(IConfiguration section, string key, double fallback) =>
            double.TryParse(section[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
    }

    /// <summary>
    /// The traction battery's real capacity from configuration, or
    /// <see cref="HvBatteryCapacity.Unknown"/> when the deployment has not said. A value of zero
    /// or less is treated as unset: it would make every state of charge meaningless rather than
    /// merely unknown.
    /// </summary>
    public static IServiceCollection AddHvBatteryCapacity(
        this IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration["HvBattery:CapacityKwh"];
        var parsed = double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out var kwh)
            && kwh > 0
                ? new HvBatteryCapacity(kwh)
                : HvBatteryCapacity.Unknown;

        return services.AddSingleton(parsed);
    }

    /// <summary>
    /// An integer setting, falling back to <paramref name="fallback"/> when it is unset, blank or
    /// unparseable. Deployments pass configuration through environment variables, where "unset"
    /// usually arrives as an empty string rather than as a missing key.
    /// </summary>
    public static int IntegerOrDefault(this IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
