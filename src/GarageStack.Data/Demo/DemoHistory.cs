using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

/// <summary>
/// The demo car's telemetry history, walked from one list of drives so that everything the
/// statistics page sets side by side agrees: the odometer, the day counters, the since-last-charge
/// counters and the state of charge all move with the same drives. The last five weeks are the
/// demo trips the trip log and the map show; the year before that is a generated commute. The
/// odometer is counted back from the live reading, as the trip log's is.
/// </summary>
internal static class DemoHistory
{
    // A little over a year, so the statistics have something to show for last year and per month.
    public const int Days = 400;

    public const double CapacityKwh = 70.0;

    private const int SlowLeakDays = 12;
    private const double SlowLeakBarPerDay = 0.02;

    // The battery's own drain while parked, which the car counts as energy used.
    private const double StandbyKwhPerHour = 0.012;

    // Charged overnight when the evening reading finds less than this, up to the charge target.
    private const double ChargeBelowPercent = 50;
    private const double ChargeTargetPercent = 80;

    // Where the car charges: at home on a wall box, and now and then at a public fast charger.
    private static readonly (double Latitude, double Longitude) HomeCharger = (52.3812, 4.9156);
    private static readonly (double Latitude, double Longitude)[] FastChargers =
    [
        (52.3676, 4.9041),
        (52.0907, 5.1214),
        (51.9244, 4.4777),
        (52.0705, 4.3007),
    ];

    // An evening charge at least this many days after the last fast one is a fast one.
    private const int DaysBetweenFastCharges = 28;

    // What a drive takes from the battery, a little more or less by traffic and weather.
    private const double KwhPerKmMin = 0.15;
    private const double KwhPerKmMax = 0.19;

    /// <summary>What the trip in progress has taken so far, at the middle of that range.</summary>
    public const double InProgressKwhPerKm = (KwhPerKmMin + KwhPerKmMax) / 2;

    // Morning, midday, after work, evening (plug-in time). 21:00 UTC is still the same day in
    // Europe, so every reading belongs to the day its counters count.
    private static readonly int[] ReadingHours = [7, 12, 17, 21];

    private readonly record struct Drive(DateTime Start, DateTime End, double Km, double KwhPerKm);

    /// <summary>One reading of a charge: the gateway publishes power in kW.</summary>
    private sealed record ChargeReading(
        string Type, bool Charging, double PowerKw, DateTime StartedAt, DateTime? EndedAt,
        (double Latitude, double Longitude) Place);

    /// <summary>The history's last state, which the live snapshot carries on from.</summary>
    public sealed record Latest(
        double SocPercent,
        double DayKm,
        double DayKwh,
        double SinceChargeKm,
        double SinceChargeKwh);

    private static readonly Lazy<(IReadOnlyList<TelemetrySnapshot> Snapshots, Latest Latest)> _built =
        new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Every reading, oldest first.</summary>
    public static IReadOnlyList<TelemetrySnapshot> Snapshots => _built.Value.Snapshots;

    public static Latest Last => _built.Value.Latest;

    private static (IReadOnlyList<TelemetrySnapshot>, Latest) Build()
    {
        var rng = new Random(42);
        var now = DateTime.UtcNow;
        var firstDay = now.Date.AddDays(-Days);
        var drives = BuildDrives(firstDay, rng);

        var odometer = DemoTrips.CurrentOdometerKm - DemoTrips.InProgressDistanceKm - drives.Sum(d => d.Km);
        var soc = ChargeTargetPercent;
        var fuel = 80.0;
        var dayKm = 0.0;
        var dayKwh = 0.0;
        var sinceKm = 0.0;
        var sinceKwh = 0.0;
        var lastFastChargeDay = -DaysBetweenFastCharges / 2;
        var nextDrive = 0;
        var lastReadingAt = firstDay;
        var lastReadingDay = DateTime.MinValue;
        var id = 100L;
        var snapshots = new List<TelemetrySnapshot>((Days + 1) * ReadingHours.Length);

        for (var day = 0; day <= Days; day++)
        {
            var date = firstDay.AddDays(day);
            foreach (var hour in ReadingHours)
            {
                var at = date.AddHours(hour).AddMinutes(rng.Next(0, 30));
                if (at > now) break;

                // The day counters start again overnight.
                if (hour == ReadingHours[0])
                    dayKm = dayKwh = 0;

                for (; nextDrive < drives.Count && drives[nextDrive].End <= at; nextDrive++)
                {
                    var drive = drives[nextDrive];
                    var kwh = drive.Km * drive.KwhPerKm;
                    odometer += drive.Km;
                    dayKm += drive.Km;
                    sinceKm += drive.Km;
                    Use(kwh);
                    fuel -= drive.Km * 0.11;
                }
                Use((at - lastReadingAt).TotalHours * StandbyKwhPerHour);
                lastReadingAt = at;
                lastReadingDay = date;

                // A weekly refuel, for the hybrid the demo can be switched to.
                if (hour == ReadingHours[^1] && day % 7 == 0) fuel = 90 + rng.Next(0, 10);
                fuel = Math.Clamp(fuel, 5, 100);

                // Plugged in in the evening when the battery is low, unless that charge would
                // still be running now: the history holds finished charges only.
                var fast = day - lastFastChargeDay >= DaysBetweenFastCharges;
                var plan = hour == ReadingHours[^1] && soc < ChargeBelowPercent ? PlanCharge(at, fast, rng) : null;
                if (plan is null || plan[^1].At.AddMinutes(2) > now)
                {
                    snapshots.Add(Reading(id++, at, day, hour, rng));
                    continue;
                }

                if (fast) lastFastChargeDay = day;
                var place = fast ? FastChargers[day / DaysBetweenFastCharges % FastChargers.Length] : HomeCharger;
                var type = fast ? "DC" : "AC";
                snapshots.Add(Reading(id++, at, day, hour, rng, new ChargeReading(type, true, plan[0].PowerKw, at, null, place)));
                foreach (var step in plan.Skip(1))
                {
                    soc = step.Soc;
                    snapshots.Add(Reading(id++, step.At, day, hour, rng, new ChargeReading(type, true, step.PowerKw, at, null, place)));
                }

                // The charger stays connected a moment after the battery reaches the target, and
                // the since-last-charge counters start again.
                var endedAt = plan[^1].At.AddMinutes(2);
                sinceKm = sinceKwh = 0;
                snapshots.Add(Reading(id++, endedAt, day, hour, rng, new ChargeReading(type, false, 0, at, endedAt, place)));
                lastReadingAt = endedAt;
            }
        }

        // The live snapshot is today's: before the first reading of the day, nothing is counted yet.
        var today = lastReadingDay == now.Date;
        return (snapshots, new Latest(
            Math.Round(soc, 1),
            today ? dayKm : 0,
            today ? dayKwh : 0,
            sinceKm,
            sinceKwh));

        void Use(double kwh)
        {
            dayKwh += kwh;
            sinceKwh += kwh;
            soc = Math.Max(5, soc - kwh / CapacityKwh * 100);
        }

        // The readings of one charge from the current level to the target: a short ramp up and a
        // steady draw on the wall box, a taper towards the target on a fast charger.
        List<(DateTime At, double Soc, double PowerKw)> PlanCharge(DateTime start, bool fast, Random random)
        {
            var startSoc = soc;
            var peakKw = fast ? 75 + random.NextDouble() * 12 : 6.8 + random.NextDouble() * 0.7;
            var efficiency = fast ? 0.94 : 0.9;
            var intervalMinutes = fast ? 5.0 : 10.0;
            var level = soc;
            var at = start;
            List<(DateTime, double, double)> steps = [(start, level, fast ? peakKw : peakKw * 0.45)];

            while (level < ChargeTargetPercent)
            {
                var supply = fast ? 1 : 0.98 + random.NextDouble() * 0.04;
                var powerKw = PowerAt(level, supply);
                var minutesToTarget = (ChargeTargetPercent - level) / 100 * CapacityKwh / (powerKw * efficiency) * 60;
                var minutes = Math.Min(intervalMinutes, minutesToTarget);
                level = Math.Min(ChargeTargetPercent, level + powerKw * efficiency * minutes / 60 / CapacityKwh * 100);
                at = at.AddMinutes(minutes);
                steps.Add((at, level, PowerAt(level, supply)));
            }
            return steps;

            double PowerAt(double current, double supply)
            {
                if (fast)
                    return peakKw * Math.Clamp((ChargeTargetPercent - current) / (ChargeTargetPercent - 55), 0.2, 1);
                var progress = (current - startSoc) / (ChargeTargetPercent - startSoc);
                return peakKw * (progress < 0.08 ? 0.82 + progress * 2.25 : 1) * supply;
            }
        }

        TelemetrySnapshot Reading(long rowId, DateTime at, int day, int hour, Random random, ChargeReading? charge = null)
        {
            var extTemp = 13.0 + (random.NextDouble() - 0.5) * 10.0;
            var frontPressure = 2.4 + (random.NextDouble() - 0.5) * 0.06;
            var rearPressure = 2.5 + (random.NextDouble() - 0.5) * 0.06;
            // The rear right has a slow puncture: over the last twelve days it loses air the other
            // three keep, for the tyre history and the slow-puncture alert to show.
            var leak = Math.Max(0, day - (Days - SlowLeakDays)) * SlowLeakBarPerDay;
            var charging = charge?.Charging == true;
            // A charge runs on into the night, past midnight in Europe: its later readings leave
            // the day counters out rather than carry the day over.
            var countsDay = charge is null || (at.Hour < 22 && at.Date == firstDay.AddDays(day));
            return new TelemetrySnapshot
            {
                Id = rowId,
                VehicleId = 1,
                RecordedAt = at,
                FuelLevelPercent = Math.Round(fuel, 1),
                FuelRangeKm = Math.Round(fuel / 100.0 * 650.0, 0),
                EvSocPercent = Math.Round(soc, 1),
                HvSocKwh = Math.Round(soc / 100.0 * CapacityKwh, 1),
                HvTotalCapacityKwh = CapacityKwh,
                PowerUsageOfDay = countsDay ? Math.Round(dayKwh, 2) : null,
                MileageOfTheDay = countsDay ? Math.Round(dayKm, 1) : null,
                PowerUsageSinceLastCharge = Math.Round(sinceKwh, 2),
                MileageSinceLastCharge = Math.Round(sinceKm, 1),
                OdometerKm = Math.Round(odometer, 1),
                IsCharging = charging,
                ChargerConnected = charge is not null || (hour == ReadingHours[0] && soc >= ChargeTargetPercent),
                ChargingType = charge?.Type,
                ChargingLastStartAt = charge?.StartedAt,
                ChargingLastEndAt = charge?.EndedAt,
                // The battery takes what the charger gives less its losses; DC bypasses the
                // onboard charger.
                HvPower = charging ? -charge!.PowerKw * (charge.Type == "DC" ? 1 : 0.9) : null,
                ObcPowerSinglePhase = charging && charge!.Type == "AC" ? charge.PowerKw : null,
                // Warmed up before the morning drive on a cold day, for the preconditioning figure.
                ClimateOn = hour == ReadingHours[0] && extTemp < 10,
                ExteriorTemperature = Math.Round(extTemp, 1),
                InteriorTemperature = Math.Round(extTemp + random.NextDouble() * 4.0, 1),
                TyrePressureFrontLeft = Math.Round(frontPressure, 2),
                TyrePressureFrontRight = Math.Round(frontPressure + (random.NextDouble() - 0.5) * 0.04, 2),
                TyrePressureRearLeft = Math.Round(rearPressure, 2),
                TyrePressureRearRight = Math.Round(rearPressure - leak + (random.NextDouble() - 0.5) * 0.04, 2),
                BatteryVoltage = 12.6 + random.NextDouble() * 0.3,
                Latitude = charge?.Place.Latitude ?? 52.3676 + (random.NextDouble() - 0.5) * 0.02,
                Longitude = charge?.Place.Longitude ?? 4.9041 + (random.NextDouble() - 0.5) * 0.02,
                Speed = 0,
                IsLocked = true,
                EngineRunning = false,
                HvBatteryActive = true,
            };
        }
    }

    // The demo trips for as far back as they go, so the statistics and the trip log tell the same
    // story; before them a commute on weekdays and the odd outing at the weekend.
    private static List<Drive> BuildDrives(DateTime firstDay, Random rng)
    {
        var finished = DemoTrips.All.Where(t => t.Id is not null).ToList();
        var drives = finished
            .Select(t => new Drive(t.StartedAt, t.EndedAt, t.DistanceKm * DemoTrips.RoadFactor, Rate(rng)))
            .ToList();

        var tripsFrom = finished.Min(t => t.StartedAt).Date;
        for (var day = firstDay; day < tripsFrom; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                if (rng.Next(3) == 0) continue;
                drives.Add(Generated(day.AddHours(10).AddMinutes(rng.Next(0, 120)), 10 + rng.NextDouble() * 50, rng));
            }
            else
            {
                var commute = 15 + rng.NextDouble() * 10;
                drives.Add(Generated(day.AddHours(7).AddMinutes(30 + rng.Next(0, 30)), commute, rng));
                drives.Add(Generated(day.AddHours(16).AddMinutes(rng.Next(0, 60)), commute, rng));
            }
        }

        return [.. drives.OrderBy(d => d.Start)];
    }

    // Town and ring road: 45 km/h on average, door to door.
    private static Drive Generated(DateTime start, double km, Random rng) =>
        new(start, start.AddHours(km / 45), km, Rate(rng));

    private static double Rate(Random rng) => KwhPerKmMin + rng.NextDouble() * (KwhPerKmMax - KwhPerKmMin);
}
