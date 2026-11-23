using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

/// <summary>
/// One demo car: what it is, where it lives, and how it is driven and charged. Its trips, its
/// telemetry history and its live reading are all built from this, so each car tells a story of its
/// own and switching between them shows a different car instead of the same numbers twice.
/// </summary>
internal sealed class DemoCar
{
    private readonly Lazy<DemoTrips> _trips;
    private readonly Lazy<DemoHistory> _history;

    public DemoCar()
    {
        _trips = new(() => new DemoTrips(this), LazyThreadSafetyMode.ExecutionAndPublication);
        _history = new(() => new DemoHistory(this), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public required Vehicle Vehicle { get; init; }

    /// <summary>Seeds the history's randomness: each car's readings differ, and stay the same on every start.</summary>
    public required int Seed { get; init; }

    /// <summary>Where the car stands overnight and charges at home.</summary>
    public required (double Latitude, double Longitude) Home { get; init; }

    /// <summary>The odometer the car reports right now.</summary>
    public required double CurrentOdometerKm { get; init; }

    /// <summary>The traction battery, in kWh, and what the car covers on a full one.</summary>
    public required double CapacityKwh { get; init; }

    public required double FullBatteryRangeKm { get; init; }

    public required double NominalVoltage { get; init; }

    /// <summary>What a drive takes from the battery, a little more or less by traffic and weather.</summary>
    public required double KwhPerKmMin { get; init; }

    public required double KwhPerKmMax { get; init; }

    /// <summary>What the trip in progress has taken so far, at the middle of that range.</summary>
    public double InProgressKwhPerKm => (KwhPerKmMin + KwhPerKmMax) / 2;

    /// <summary>Plugged in in the evening when the battery is below this, and charged up to the target.</summary>
    public required double ChargeBelowPercent { get; init; }

    public required double ChargeTargetPercent { get; init; }

    /// <summary>What the wall box at home gives, in kW.</summary>
    public required double HomeChargerKw { get; init; }

    /// <summary>Public fast chargers the car calls at now and then; none for a car that never fast-charges.</summary>
    public IReadOnlyList<(double Latitude, double Longitude)> FastChargers { get; init; } = [];

    /// <summary>The fuel tank and what the engine burns. A battery car keeps one too, for when the
    /// settings switch it to a hybrid.</summary>
    public required double FuelTankLitres { get; init; }

    public required double LitresPer100Km { get; init; }

    /// <summary>What a full tank covers.</summary>
    public double FuelRangeKm => FuelTankLitres / LitresPer100Km * 100;

    /// <summary>
    /// A plug-in hybrid drives on the battery until it is down to <see cref="HybridFloorPercent"/>
    /// and on fuel after that. A battery car burns its make-believe fuel on every kilometre.
    /// </summary>
    public bool ElectricFirst { get; init; }

    public double HybridFloorPercent { get; init; } = 15;

    /// <summary>The generated year before the demo trips: a weekday commute each way, and the odd weekend outing.</summary>
    public required (double Min, double Max) CommuteKm { get; init; }

    public required (double Min, double Max) OutingKm { get; init; }

    /// <summary>Tyre pressures when nothing is wrong, in bar.</summary>
    public required double FrontTyreBar { get; init; }

    public required double RearTyreBar { get; init; }

    /// <summary>Days the rear right tyre has been losing air, for the slow-puncture alert; 0 for none.</summary>
    public int SlowLeakDays { get; init; }

    /// <summary>The 12 V battery's resting voltage.</summary>
    public required double AuxBatteryVolts { get; init; }

    /// <summary>The charging schedule the car reports: when it starts and stops.</summary>
    public required (string Start, string End) ChargingSchedule { get; init; }

    /// <summary>
    /// The car's trips, oldest first: finished ones with an id, and at most one in progress (no id),
    /// started last. Built from the moment the demo starts.
    /// </summary>
    public required Func<DateTime, IReadOnlyList<TripDto>> BuildTrips { get; init; }

    /// <summary>The purpose a visitor finds on each trip, by id; trips left out are unclassified.</summary>
    public IReadOnlyDictionary<long, TripPurpose> Purposes { get; init; } = new Dictionary<long, TripPurpose>();

    public IReadOnlyDictionary<long, string> Notes { get; init; } = new Dictionary<long, string>();

    /// <summary>Trips a phone travelled in, fused as the Worker fuses them.</summary>
    public IReadOnlySet<long> PhoneTrips { get; init; } = new HashSet<long>();

    /// <summary>What the day counter counts per 100 km on a trip, by trip id: the trip log's figure.</summary>
    public required Func<long, double> CounterPer100Km { get; init; }

    public DemoTrips Trips => _trips.Value;

    public DemoHistory History => _history.Value;
}
