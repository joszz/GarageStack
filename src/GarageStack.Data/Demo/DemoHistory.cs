using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

/// <summary>
/// One demo car's telemetry history, walked from one list of drives so that everything the
/// statistics page sets side by side agrees: the odometer, the day counters, the since-last-charge
/// counters, the fuel and the state of charge all move with the same drives. The last five weeks are
/// the car's demo trips, which the trip log and the map show; the year before that is a generated
/// commute. The odometer is counted back from the live reading, as the trip log's is.
/// </summary>
internal sealed class DemoHistory
{
    // A little over a year, so the statistics have something to show for last year and per month.
    public const int Days = 400;

    private const double SlowLeakBarPerDay = 0.02;

    // The battery's own drain while parked, which the car counts as energy used.
    private const double StandbyKwhPerHour = 0.012;

    // An evening charge at least this many days after the last fast one is a fast one, for a car
    // that calls at fast chargers.
    private const int DaysBetweenFastCharges = 28;

    // Filled up at the evening reading once the tank is below this.
    private const double RefuelBelowPercent = 20;

    // Plugged in the evening before a long day out too, when the next day's drives would otherwise
    // bring the battery home below this.
    private const double ArriveAbovePercent = 20;

    // Below this outside temperature a drive takes more, by this share per degree: the heater, a
    // cold battery and denser air, so the statistics' temperature chart has a winter to show.
    private const double MildFromCelsius = 15;
    private const double ColdSharePerDegree = 0.012;

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
        double FuelPercent,
        double DayKm,
        double DayKwh,
        double SinceChargeKm,
        double SinceChargeKwh);

    private readonly DemoCar _car;

    public DemoHistory(DemoCar car)
    {
        _car = car;
        (Snapshots, Last) = Build();
    }

    /// <summary>Every reading, oldest first.</summary>
    public IReadOnlyList<TelemetrySnapshot> Snapshots { get; }

    public Latest Last { get; }

    private (IReadOnlyList<TelemetrySnapshot>, Latest) Build()
    {
        var car = _car;
        var rng = new Random(car.Seed);
        var now = DateTime.UtcNow;
        var firstDay = now.Date.AddDays(-Days);
        var drives = BuildDrives(firstDay, rng);
        // On the days of the demo trips the map shows where the car went: it charges at home on
        // those evenings, never at a fast charger it was not at, and not at all on a night away.
        var tripsFrom = car.Trips.All.Where(t => t.Id is not null).Min(t => t.StartedAt).Date;

        var odometer = car.CurrentOdometerKm - car.Trips.InProgressDistanceKm - drives.Sum(d => d.Km);
        var soc = car.ChargeTargetPercent;
        var fuel = 80.0;
        var dayKm = 0.0;
        var dayKwh = 0.0;
        var sinceKm = 0.0;
        var sinceKwh = 0.0;
        var lastFastChargeDay = -DaysBetweenFastCharges / 2;
        var nextDrive = 0;
        var lastReadingAt = firstDay;
        var lastReadingDay = DateTime.MinValue;
        var id = car.Vehicle.Id * 1_000_000L + 100;
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
                    var kwh = drive.Km * drive.KwhPerKm * (1 + Math.Max(0, MildFromCelsius - Outside(drive.End)) * ColdSharePerDegree);
                    odometer += drive.Km;
                    dayKm += drive.Km;
                    sinceKm += drive.Km;

                    // A plug-in hybrid drives on the battery while it lasts and on fuel after that.
                    var electricKwh = car.ElectricFirst
                        ? Math.Clamp((soc - car.HybridFloorPercent) / 100 * car.CapacityKwh, 0, kwh)
                        : kwh;
                    var fuelKm = car.ElectricFirst ? drive.Km * (1 - electricKwh / kwh) : drive.Km;
                    Use(electricKwh);
                    fuel -= fuelKm * car.LitresPer100Km / car.FuelTankLitres;
                }
                Use((at - lastReadingAt).TotalHours * StandbyKwhPerHour);
                lastReadingAt = at;
                lastReadingDay = date;

                if (hour == ReadingHours[^1] && fuel < RefuelBelowPercent) fuel = 90 + rng.Next(0, 10);
                fuel = Math.Clamp(fuel, 5, 100);

                // Plugged in in the evening when the battery is low, unless that charge would
                // still be running now: the history holds finished charges only.
                var fast = car.FastChargers.Count > 0 && date < tripsFrom && day - lastFastChargeDay >= DaysBetweenFastCharges;
                var plugIn = (soc < car.ChargeBelowPercent || soc - PercentNeededNextDay(at) < ArriveAbovePercent) && !AwayFromHome(at);
                var plan = hour == ReadingHours[^1] && plugIn ? PlanCharge(at, fast, rng) : null;
                if (plan is null || plan[^1].At.AddMinutes(2) > now)
                {
                    snapshots.Add(Reading(id++, at, day, hour, rng));
                    continue;
                }

                if (fast) lastFastChargeDay = day;
                var place = fast ? car.FastChargers[day / DaysBetweenFastCharges % car.FastChargers.Count] : car.Home;
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
            Math.Round(fuel, 1),
            today ? dayKm : 0,
            today ? dayKwh : 0,
            sinceKm,
            sinceKwh));

        // The car's last demo trip ended away from home, so there is nothing to plug in to.
        bool AwayFromHome(DateTime at) =>
            car.Trips.All.LastOrDefault(t => t.Id is not null && t.EndedAt <= at) is { } last &&
            GeoHelper.Haversine(last.Points[^1].Latitude, last.Points[^1].Longitude, car.Home.Latitude, car.Home.Longitude) > 0.5;

        // What the drives of the coming day take from the battery, as a share of it.
        double PercentNeededNextDay(DateTime evening) =>
            drives.Where(d => d.Start > evening && d.Start <= evening.AddDays(1))
                .Sum(d => d.Km * d.KwhPerKm) / car.CapacityKwh * 100;

        void Use(double kwh)
        {
            dayKwh += kwh;
            sinceKwh += kwh;
            soc = Math.Max(5, soc - kwh / car.CapacityKwh * 100);
        }

        // The readings of one charge from the current level to the target: a short ramp up and a
        // steady draw on the wall box, a taper towards the target on a fast charger.
        List<(DateTime At, double Soc, double PowerKw)> PlanCharge(DateTime start, bool fast, Random random)
        {
            var target = car.ChargeTargetPercent;
            var startSoc = soc;
            var peakKw = fast ? 75 + random.NextDouble() * 12 : car.HomeChargerKw * (1 + random.NextDouble() * 0.1);
            var efficiency = fast ? 0.94 : 0.9;
            var intervalMinutes = fast ? 5.0 : 10.0;
            var level = soc;
            var at = start;
            List<(DateTime, double, double)> steps = [(start, level, fast ? peakKw : peakKw * 0.45)];

            while (level < target)
            {
                var supply = fast ? 1 : 0.98 + random.NextDouble() * 0.04;
                var powerKw = PowerAt(level, supply);
                var minutesToTarget = (target - level) / 100 * car.CapacityKwh / (powerKw * efficiency) * 60;
                var minutes = Math.Min(intervalMinutes, minutesToTarget);
                level = Math.Min(target, level + powerKw * efficiency * minutes / 60 / car.CapacityKwh * 100);
                at = at.AddMinutes(minutes);
                steps.Add((at, level, PowerAt(level, supply)));
            }
            return steps;

            double PowerAt(double current, double supply)
            {
                if (fast)
                    return peakKw * Math.Clamp((target - current) / (target - 55), 0.2, 1);
                var progress = (current - startSoc) / (target - startSoc);
                return peakKw * (progress < 0.08 ? 0.82 + progress * 2.25 : 1) * supply;
            }
        }

        TelemetrySnapshot Reading(long rowId, DateTime at, int day, int hour, Random random, ChargeReading? charge = null)
        {
            var extTemp = Outside(at) + (random.NextDouble() - 0.5) * 6.0;
            var frontPressure = car.FrontTyreBar + (random.NextDouble() - 0.5) * 0.06;
            var rearPressure = car.RearTyreBar + (random.NextDouble() - 0.5) * 0.06;
            // A car with a slow puncture loses air in the rear right tyre over its last days, which
            // the other three keep, for the tyre history and the slow-puncture alert to show.
            var leak = car.SlowLeakDays > 0 ? Math.Max(0, day - (Days - car.SlowLeakDays)) * SlowLeakBarPerDay : 0;
            var charging = charge?.Charging == true;
            // A charge runs on into the night, past midnight in Europe: its later readings leave
            // the day counters out rather than carry the day over.
            var countsDay = charge is null || (at.Hour < 22 && at.Date == firstDay.AddDays(day));
            return new TelemetrySnapshot
            {
                Id = rowId,
                VehicleId = car.Vehicle.Id,
                RecordedAt = at,
                FuelLevelPercent = Math.Round(fuel, 1),
                FuelRangeKm = Math.Round(fuel / 100.0 * car.FuelRangeKm, 0),
                EvSocPercent = Math.Round(soc, 1),
                HvSocKwh = Math.Round(soc / 100.0 * car.CapacityKwh, 1),
                HvTotalCapacityKwh = car.CapacityKwh,
                PowerUsageOfDay = countsDay ? Math.Round(dayKwh, 2) : null,
                MileageOfTheDay = countsDay ? Math.Round(dayKm, 1) : null,
                PowerUsageSinceLastCharge = Math.Round(sinceKwh, 2),
                MileageSinceLastCharge = Math.Round(sinceKm, 1),
                OdometerKm = Math.Round(odometer, 1),
                IsCharging = charging,
                ChargerConnected = charge is not null || (hour == ReadingHours[0] && soc >= car.ChargeTargetPercent),
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
                BatteryVoltage = car.AuxBatteryVolts + random.NextDouble() * 0.3,
                Latitude = charge?.Place.Latitude ?? car.Home.Latitude + (random.NextDouble() - 0.5) * 0.02,
                Longitude = charge?.Place.Longitude ?? car.Home.Longitude + (random.NextDouble() - 0.5) * 0.02,
                Speed = 0,
                IsLocked = true,
                EngineRunning = false,
                HvBatteryActive = true,
            };
        }
    }

    /// <summary>The rear right tyre's pressure now, after the slow puncture's days.</summary>
    public double RearRightTyreBarNow => _car.RearTyreBar - _car.SlowLeakDays * SlowLeakBarPerDay;

    // The outside temperature by season and time of day: about 3 °C on a January night, 22 °C on a
    // July afternoon.
    private static double Outside(DateTime at)
    {
        var season = -Math.Cos(2 * Math.PI * (at.DayOfYear - 15) / 365.0);
        var afternoon = -Math.Cos(2 * Math.PI * (at.Hour - 3) / 24.0);
        return 11 + season * 8 + afternoon * 3;
    }

    // The car's demo trips for as far back as they go, so the statistics and the trip log tell the
    // same story; before them a commute on weekdays and the odd outing at the weekend.
    private List<Drive> BuildDrives(DateTime firstDay, Random rng)
    {
        var car = _car;
        var finished = car.Trips.All.Where(t => t.Id is not null).ToList();
        var drives = finished
            .Select(t => new Drive(t.StartedAt, t.EndedAt, t.DistanceKm * DemoTrips.RoadFactor, Rate(rng)))
            .ToList();

        var tripsFrom = finished.Min(t => t.StartedAt).Date;
        for (var day = firstDay; day < tripsFrom; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                if (rng.Next(3) == 0) continue;
                drives.Add(Generated(day.AddHours(10).AddMinutes(rng.Next(0, 120)), Between(car.OutingKm, rng), rng));
            }
            else
            {
                var commute = Between(car.CommuteKm, rng);
                drives.Add(Generated(day.AddHours(7).AddMinutes(30 + rng.Next(0, 30)), commute, rng));
                drives.Add(Generated(day.AddHours(16).AddMinutes(rng.Next(0, 60)), commute, rng));
            }
        }

        return [.. drives.OrderBy(d => d.Start)];
    }

    private static double Between((double Min, double Max) range, Random rng) =>
        range.Min + rng.NextDouble() * (range.Max - range.Min);

    // Town and ring road: 45 km/h on average, door to door.
    private Drive Generated(DateTime start, double km, Random rng) =>
        new(start, start.AddHours(km / 45), km, Rate(rng));

    private double Rate(Random rng) => Between((_car.KwhPerKmMin, _car.KwhPerKmMax), rng);
}
