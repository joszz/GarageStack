using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Folds a period of telemetry into one <see cref="DailyStat"/> per local day, from every reading
/// rather than a sample of them, so a day's counter total is exact however long the period. Fed in
/// arrival order: a day's counter readings are read as they came.
/// </summary>
public sealed class DailyStatsBuilder(TimeZoneInfo zone, DateOnly from, DateOnly to, DateOnly today)
{
    // A reading below this is taken for a counter reset rather than noise.
    private const double ResetNoiseFloor = 0.05;

    // A reading below this fraction of the day's running peak is taken for a counter reset.
    private const double ResetPeakFraction = 0.05;

    private readonly Dictionary<DateOnly, Day> _days = [];

    private sealed class Mean
    {
        private double _sum;
        private int _count;

        public void Add(double? value)
        {
            if (value is not { } v || !double.IsFinite(v)) return;
            _sum += v;
            _count++;
        }

        public double? Value => _count > 0 ? _sum / _count : null;
    }

    private sealed class Day
    {
        public readonly Mean EvSoc = new(), Fuel = new(), FrontLeft = new(), FrontRight = new(),
            RearLeft = new(), RearRight = new(), Voltage = new();
        public readonly List<double> Counter = [];
    }

    /// <summary>The UTC instants the local days from <paramref name="first"/> to <paramref name="last"/> span, end exclusive.</summary>
    public static (DateTime Start, DateTime End) UtcRange(DateOnly first, DateOnly last, TimeZoneInfo zone) =>
        (ZoneTime.StartOfDay(first, zone), ZoneTime.StartOfDay(last.AddDays(1), zone));

    public void Add(TelemetryHistoryPoint reading)
    {
        var utc = DateTime.SpecifyKind(reading.RecordedAt, DateTimeKind.Utc);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
        if (date < from || date > to) return;

        if (!_days.TryGetValue(date, out var day)) _days[date] = day = new Day();
        day.EvSoc.Add(reading.EvSocPercent);
        day.Fuel.Add(reading.FuelLevelPercent);
        day.FrontLeft.Add(reading.TyrePressureFrontLeft);
        day.FrontRight.Add(reading.TyrePressureFrontRight);
        day.RearLeft.Add(reading.TyrePressureRearLeft);
        day.RearRight.Add(reading.TyrePressureRearRight);
        day.Voltage.Add(reading.BatteryVoltage);
        if (reading.PowerUsageOfDay is { } counter && double.IsFinite(counter)) day.Counter.Add(counter);
    }

    /// <summary>Every day of the period up to today, a day without readings included, oldest first.</summary>
    public IReadOnlyList<DailyStat> Build()
    {
        var last = to < today ? to : today;
        var stats = new List<DailyStat>();
        for (var date = from; date <= last; date = date.AddDays(1))
        {
            stats.Add(_days.TryGetValue(date, out var day)
                ? new DailyStat(date, day.EvSoc.Value, day.Fuel.Value, day.FrontLeft.Value, day.FrontRight.Value,
                    day.RearLeft.Value, day.RearRight.Value, day.Voltage.Value, CounterTotal(day.Counter, date == today))
                : new DailyStat(date, null, null, null, null, null, null, null, null));
        }
        return stats;
    }

    /// <summary>
    /// One day's total from its cumulative counter readings, oldest first. A finished day's total is
    /// its peak. Today is still running: the counter can still hold yesterday's total until the car
    /// resets it, so only what came after a reset, or the rise since the first reading, counts.
    /// </summary>
    public static double? CounterTotal(IReadOnlyList<double> readings, bool isPartialDay)
    {
        if (readings.Count == 0) return null;
        if (!isPartialDay) return readings.Max();

        var lastReset = -1;
        var peak = readings[0];
        for (var i = 1; i < readings.Count; i++)
        {
            peak = Math.Max(peak, readings[i - 1]);
            if (readings[i] < Math.Max(peak * ResetPeakFraction, ResetNoiseFloor)) lastReset = i;
        }

        if (lastReset >= 0)
        {
            var usage = readings.Skip(lastReset).Max();
            return usage > 0 ? usage : null;
        }

        var delta = readings.Max() - readings.Min();
        return delta > 0 ? delta : null;
    }
}
