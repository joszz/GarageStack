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

    // The since-last-charge reading before the one being added, across days, so a day's share of
    // that counter includes what it rose by while the car slept since the day before.
    private double? _lastSinceCharge;

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
        public readonly List<double> Counter = [], Distance = [];
        public readonly EnergyCheck Energy = new();
    }

    /// <summary>The UTC instants the local days from <paramref name="first"/> to <paramref name="last"/> span, end exclusive.</summary>
    public static (DateTime Start, DateTime End) UtcRange(DateOnly first, DateOnly last, TimeZoneInfo zone) =>
        (ZoneTime.StartOfDay(first, zone), ZoneTime.StartOfDay(last.AddDays(1), zone));

    /// <summary>
    /// Folds one reading into its day. A reading from before the period only carries its
    /// since-last-charge value forward, so the first day's share of that counter can start from it;
    /// the repository passes the last one before the period for that.
    /// </summary>
    public void Add(TelemetryHistoryPoint reading)
    {
        var utc = DateTime.SpecifyKind(reading.RecordedAt, DateTimeKind.Utc);
        var date = ZoneTime.DateOf(utc, zone);
        var since = reading.PowerUsageSinceLastCharge is { } s && double.IsFinite(s) ? s : (double?)null;
        if (date < from)
        {
            _lastSinceCharge = since ?? _lastSinceCharge;
            return;
        }
        if (date > to) return;

        if (!_days.TryGetValue(date, out var day)) _days[date] = day = new Day();
        day.Energy.Add(reading.PowerUsageOfDay is { } c && double.IsFinite(c) ? c : null, since, _lastSinceCharge,
            reading.IsCharging == true);
        _lastSinceCharge = since ?? _lastSinceCharge;
        day.EvSoc.Add(reading.EvSocPercent);
        day.Fuel.Add(reading.FuelLevelPercent);
        day.FrontLeft.Add(reading.TyrePressureFrontLeft);
        day.FrontRight.Add(reading.TyrePressureFrontRight);
        day.RearLeft.Add(reading.TyrePressureRearLeft);
        day.RearRight.Add(reading.TyrePressureRearRight);
        day.Voltage.Add(reading.BatteryVoltage);
        if (reading.PowerUsageOfDay is { } counter && double.IsFinite(counter)) day.Counter.Add(counter);
        if (reading.MileageOfTheDay is { } distance && double.IsFinite(distance)) day.Distance.Add(distance);
    }

    /// <summary>Every day of the period up to today, a day without readings included, oldest first.</summary>
    public IReadOnlyList<DailyStat> Build()
    {
        var last = to < today ? to : today;
        var stats = new List<DailyStat>();
        for (var date = from; date <= last; date = date.AddDays(1))
        {
            if (!_days.TryGetValue(date, out var day))
            {
                stats.Add(new DailyStat(date, null, null, null, null, null, null, null, null));
                continue;
            }
            var counter = CounterTotal(day.Counter, date == today);
            // The distance counter resets with the energy counter, so it is read the same way. A
            // parked day reads 0 rather than null: its energy is standby use, part of the total.
            var distance = DistanceTotal(day.Distance, date == today);
            stats.Add(new DailyStat(date, day.EvSoc.Value, day.Fuel.Value, day.FrontLeft.Value, day.FrontRight.Value,
                day.RearLeft.Value, day.RearRight.Value, day.Voltage.Value, counter,
                day.Energy.SinceChargeTotal, day.Energy.IsUnbacked(counter), distance));
        }
        return stats;
    }

    /// <summary>
    /// One day of the two energy counters side by side. Both come out of the same charge status
    /// message, so on a sound car the day counter rises with the since-last-charge counter. The
    /// MG ZS EV in issue #551 does not: its day counter gains 33 to 35 kWh at some polls while the
    /// car is parked, with the battery's charge and the since-last-charge counter unmoved, which
    /// added up to 134 kWh on a 4 km day. The since-last-charge counter on that car matches the
    /// state of charge drop to the hundredth, so it is the one to check against.
    /// </summary>
    private sealed class EnergyCheck
    {
        // The since-last-charge counter halving, and by more than this, is a charge resetting it
        // rather than regenerative braking taking a little back.
        private const double SinceResetFloor = 0.5;

        // How far the day counter may rise beyond the since-last-charge counter: a margin for
        // rounding, on top of the battery counter's own rise for the regeneration it nets out
        // and the day counter may not.
        private const double Margin = 1.0;

        // The most a day counter may hold, as a multiple of the highest since-last-charge reading
        // of a day without a charge, before the margin: the day's use is part of what left the
        // battery since its last charge, with room for regeneration again.
        private const double SincePeakFactor = 1.5;

        private (double Counter, double Since)? _lastPair;
        private double _runCounter, _runSince, _backed, _unbacked, _sinceTotal, _sincePeak;
        private bool _sinceSeen, _charged;

        /// <summary>What the since-last-charge counter says the day used, or null without a reading of it.</summary>
        public double? SinceChargeTotal => _sinceSeen ? Math.Round(Math.Max(_sinceTotal, 0), 2) : null;

        public static bool IsSinceReset(double before, double after) =>
            after < before / 2 && before - after > SinceResetFloor;

        public void Add(double? counter, double? since, double? previousSince, bool charging)
        {
            if (charging) _charged = true;
            if (since is not { } s) return;

            _sinceSeen = true;
            _sincePeak = Math.Max(_sincePeak, s);
            if (previousSince is { } p)
            {
                // After a charge the counter starts again from zero, so all of it is new use.
                if (IsSinceReset(p, s))
                {
                    _charged = true;
                    _sinceTotal += s;
                }
                else
                {
                    _sinceTotal += s - p;
                }
            }

            if (counter is not { } c) return;
            if (_lastPair is { } last)
            {
                if (c < last.Counter - ResetNoiseFloor || IsSinceReset(last.Since, s))
                {
                    CloseRun();
                }
                else
                {
                    _runCounter += c - last.Counter;
                    _runSince += s - last.Since;
                }
            }
            _lastPair = (c, s);
        }

        // A run is a stretch in which neither counter reset, so their rises compare like for like.
        // Comparing whole runs rather than single steps leaves room for one counter updating a
        // poll before the other.
        private void CloseRun()
        {
            var since = Math.Max(_runSince, 0);
            _backed += since;
            _unbacked += Math.Max(_runCounter - since, 0);
            _runCounter = _runSince = 0;
        }

        /// <summary>
        /// Whether the day counter rose by more than the since-last-charge counter allows, judged
        /// on its rises within the day, or, for a day without a charge, on its total against the
        /// energy that had left the battery since the last one.
        /// </summary>
        public bool IsUnbacked(double? counterTotal)
        {
            CloseRun();
            if (_unbacked > Math.Max(Margin, _backed)) return true;
            return !_charged && _sinceSeen && counterTotal is { } total && total > _sincePeak * SincePeakFactor + Margin;
        }
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

    /// <summary>
    /// One day's distance off the distance-of-the-day counter, read as <see cref="CounterTotal"/>
    /// reads the energy counter, except that today without driving yet is 0 km rather than nothing.
    /// </summary>
    public static double? DistanceTotal(IReadOnlyList<double> readings, bool isPartialDay) =>
        readings.Count == 0 ? null : CounterTotal(readings, isPartialDay) ?? 0;
}
