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

    // What a drive takes from the battery, a little more or less by traffic and weather.
    private const double KwhPerKmMin = 0.15;
    private const double KwhPerKmMax = 0.19;

    /// <summary>What the trip in progress has taken so far, at the middle of that range.</summary>
    public const double InProgressKwhPerKm = (KwhPerKmMin + KwhPerKmMax) / 2;

    // Morning, midday, after work, evening (plug-in time). 21:00 UTC is still the same day in
    // Europe, so every reading belongs to the day its counters count.
    private static readonly int[] ReadingHours = [7, 12, 17, 21];

    private readonly record struct Drive(DateTime Start, DateTime End, double Km, double KwhPerKm);

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
        var chargingTonight = false;
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

                if (hour == ReadingHours[0])
                {
                    // The day counters start again overnight, and a night on the charger has
                    // filled the battery and started the since-last-charge counters again.
                    dayKm = dayKwh = 0;
                    if (chargingTonight)
                    {
                        soc = ChargeTargetPercent;
                        sinceKm = sinceKwh = 0;
                        chargingTonight = false;
                    }
                }

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

                if (hour == ReadingHours[^1])
                {
                    chargingTonight = soc < ChargeBelowPercent;
                    // A weekly refuel, for the hybrid the demo can be switched to.
                    if (day % 7 == 0) fuel = 90 + rng.Next(0, 10);
                }
                fuel = Math.Clamp(fuel, 5, 100);

                snapshots.Add(Reading(id++, at, day, hour, rng));
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

        TelemetrySnapshot Reading(long rowId, DateTime at, int day, int hour, Random random)
        {
            var extTemp = 13.0 + (random.NextDouble() - 0.5) * 10.0;
            var frontPressure = 2.4 + (random.NextDouble() - 0.5) * 0.06;
            var rearPressure = 2.5 + (random.NextDouble() - 0.5) * 0.06;
            // The rear right has a slow puncture: over the last twelve days it loses air the other
            // three keep, for the tyre history and the slow-puncture alert to show.
            var leak = Math.Max(0, day - (Days - SlowLeakDays)) * SlowLeakBarPerDay;
            var charging = hour == ReadingHours[^1] && chargingTonight;
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
                PowerUsageOfDay = Math.Round(dayKwh, 2),
                MileageOfTheDay = Math.Round(dayKm, 1),
                PowerUsageSinceLastCharge = Math.Round(sinceKwh, 2),
                MileageSinceLastCharge = Math.Round(sinceKm, 1),
                OdometerKm = Math.Round(odometer, 1),
                IsCharging = charging,
                ChargerConnected = charging || (hour == ReadingHours[0] && soc >= ChargeTargetPercent),
                // Warmed up before the morning drive on a cold day, for the preconditioning figure.
                ClimateOn = hour == ReadingHours[0] && extTemp < 10,
                ExteriorTemperature = Math.Round(extTemp, 1),
                InteriorTemperature = Math.Round(extTemp + random.NextDouble() * 4.0, 1),
                TyrePressureFrontLeft = Math.Round(frontPressure, 2),
                TyrePressureFrontRight = Math.Round(frontPressure + (random.NextDouble() - 0.5) * 0.04, 2),
                TyrePressureRearLeft = Math.Round(rearPressure, 2),
                TyrePressureRearRight = Math.Round(rearPressure - leak + (random.NextDouble() - 0.5) * 0.04, 2),
                BatteryVoltage = 12.6 + random.NextDouble() * 0.3,
                Latitude = 52.3676 + (random.NextDouble() - 0.5) * 0.02,
                Longitude = 4.9041 + (random.NextDouble() - 0.5) * 0.02,
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
