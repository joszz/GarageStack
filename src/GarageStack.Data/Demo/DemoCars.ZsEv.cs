using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

internal static partial class DemoCars
{
    /// <summary>
    /// The MG ZS EV: a battery car around Amsterdam, out on the road right now. It charges at home
    /// when the battery runs low and now and then at a public fast charger, and its rear right tyre
    /// has a slow puncture.
    /// </summary>
    public static DemoCar ZsEv { get; } = new()
    {
        Vehicle = DemoVehicleRepository.DemoVehicle,
        Seed = 42,
        Home = (52.3676, 4.9041),
        CurrentOdometerKm = 24852,
        CapacityKwh = 70.0,
        FullBatteryRangeKm = 400.0,
        NominalVoltage = 386.0,
        KwhPerKmMin = 0.15,
        KwhPerKmMax = 0.19,
        ChargeBelowPercent = 50,
        ChargeTargetPercent = 80,
        HomeChargerKw = 6.8,
        FastChargers =
        [
            (52.3676, 4.9041),
            (52.0907, 5.1214),
            (51.9244, 4.4777),
            (52.0705, 4.3007),
        ],
        FuelTankLitres = 45,
        LitresPer100Km = 6.0,
        CommuteKm = (15, 25),
        OutingKm = (10, 60),
        FrontTyreBar = 2.4,
        RearTyreBar = 2.5,
        SlowLeakDays = 12,
        AuxBatteryVolts = 12.6,
        ChargingSchedule = ("00:00", "07:00"),
        BuildTrips = ZsEvTrips,
        // A few of each purpose, with the oldest trips left unclassified so the demo shows every
        // state a trip log line can be in.
        Purposes = new Dictionary<long, TripPurpose>
        {
            [1] = TripPurpose.Business,
            [2] = TripPurpose.Business,
            [3] = TripPurpose.Private,
            [4] = TripPurpose.Business,
            [5] = TripPurpose.Private,
            [6] = TripPurpose.Private,
            [7] = TripPurpose.Commute,
        },
        Notes = new Dictionary<long, string> { [4] = "Client meeting" },
        // Trips a phone travelled in, fused as the Worker fuses them (Dawarich or the installed
        // app), so the map's dotted phone stretches and its Phone GPS layer show in the demo too.
        PhoneTrips = new HashSet<long> { 4, 10 },
        // The trip log shows a trip's fuel only on a plain hybrid, so each trip's counter is a
        // hybrid's: hundredths of a litre, which per km is L/100 km. Between 5.0 and 7.3 by trip, as
        // traffic and weather would have it; switch the demo car to a hybrid in the settings to see it.
        CounterPer100Km = id => 5.0 + id * 7 % 24 / 10.0,
    };

    // Finished trips start at a time of day, counted from midnight, so they fall in daytime whenever
    // the demo starts. Only the trip in progress runs from now.
    private static List<TripDto> ZsEvTrips(DateTime now)
    {
        var today = now.Date;
        return
        [
            // All waypoints sourced from OSRM road routing, so the coordinates follow real roads.
            // Amsterdam to Schiphol.
            DemoTrips.Trip(today.AddDays(-2).AddHours(8), id: 1,
            [
                (52.3768, 4.9006, 30),
                (52.3783, 4.9049, 40),
                (52.3830, 4.8933, 70),
                (52.3931, 4.8756, 90),
                (52.3919, 4.8434, 90),
                (52.3807, 4.8447, 80),
                (52.3727, 4.8421, 90),
                (52.3525, 4.8425, 100),
                (52.3402, 4.8408, 110),
                (52.3381, 4.8128, 110),
                (52.3279, 4.7784, 110),
                (52.3078, 4.7471, 90),
                (52.3090, 4.7635, 30),
            ]),
            // Amsterdam to Haarlem.
            DemoTrips.Trip(today.AddDays(-5).AddHours(9), id: 2,
            [
                (52.3676, 4.9041, 25),
                (52.3625, 4.9071, 40),
                (52.3501, 4.9162, 75),
                (52.3461, 4.9281, 90),
                (52.3383, 4.9392, 90),
                (52.3375, 4.8908, 80),
                (52.3381, 4.8471, 90),
                (52.3609, 4.7328, 100),
                (52.3725, 4.7111, 80),
                (52.3831, 4.7081, 50),
                (52.3871, 4.6458, 20),
            ]),
            // A drive through town.
            DemoTrips.Trip(today.AddDays(-8).AddHours(14), id: 3,
            [
                (52.3676, 4.9041, 25),
                (52.3618, 4.9075, 30),
                (52.3487, 4.9182, 50),
                (52.3383, 4.9392, 55),
                (52.3469, 4.9269, 50),
                (52.3496, 4.9170, 45),
                (52.3778, 4.9082, 35),
                (52.3702, 4.8958, 30),
                (52.3676, 4.9041, 20),
            ]),
            // Amsterdam to Utrecht.
            DemoTrips.Trip(today.AddDays(-12).AddHours(10), id: 4,
            [
                (52.3676, 4.9041, 30),
                (52.3516, 4.9137, 55),
                (52.3383, 4.9393, 90),
                (52.3275, 4.9103, 90),
                (52.2753, 4.9562, 110),
                (52.2236, 4.9851, 120),
                (52.1663, 4.9872, 120),
                (52.1324, 5.0101, 110),
                (52.1171, 5.0329, 100),
                (52.1283, 5.0438, 90),
                (52.1365, 5.0796, 90),
                (52.1277, 5.1056, 90),
                (52.1183, 5.1283, 70),
                (52.1137, 5.1220, 60),
                (52.0907, 5.1215, 30),
            ]),
            // The way back, which no poll caught: the car stood in Utrecht and was next seen at home.
            DemoTrips.Unseen(today.AddDays(-12).AddHours(17), id: 11, (52.0907, 5.1215), (52.3676, 4.9041), TimeSpan.FromMinutes(45)),
            // Amsterdam to Almere.
            DemoTrips.Trip(today.AddDays(-18).AddHours(16), id: 5,
            [
                (52.3676, 4.9041, 30),
                (52.3516, 4.9137, 60),
                (52.3482, 4.9249, 90),
                (52.3365, 4.9429, 90),
                (52.3402, 4.9520, 90),
                (52.3493, 4.9617, 100),
                (52.3453, 4.9776, 110),
                (52.3337, 4.9992, 110),
                (52.3323, 5.0193, 110),
                (52.3229, 5.0670, 110),
                (52.3144, 5.1092, 100),
                (52.3215, 5.1312, 90),
                (52.3342, 5.1572, 90),
                (52.3477, 5.1903, 80),
                (52.3686, 5.2045, 60),
                (52.3702, 5.2159, 30),
            ]),
            // Den Haag to Delft.
            DemoTrips.Trip(today.AddDays(-3).AddHours(17), id: 6,
            [
                (52.0707, 4.3008, 20),
                (52.0674, 4.3034, 55),
                (52.0641, 4.3107, 80),
                (52.0505, 4.3137, 90),
                (52.0346, 4.3288, 75),
                (52.0291, 4.3372, 70),
                (52.0227, 4.3471, 55),
                (52.0117, 4.3573, 20),
            ]),
            // Zaandam to Amsterdam.
            DemoTrips.Trip(today.AddDays(-7).AddHours(8), id: 7,
            [
                (52.4379, 4.8250, 20),
                (52.4296, 4.8254, 50),
                (52.4282, 4.8358, 65),
                (52.4310, 4.8552, 75),
                (52.4316, 4.8629, 80),
                (52.4265, 4.8758, 80),
                (52.4221, 4.9044, 75),
                (52.4183, 4.9129, 70),
                (52.3840, 4.9108, 80),
                (52.3743, 4.9122, 60),
                (52.3676, 4.9041, 25),
            ]),
            // Amsterdam to Amstelveen.
            DemoTrips.Trip(today.AddDays(-20).AddHours(10), id: 8,
            [
                (52.3676, 4.9041, 25),
                (52.3516, 4.9137, 55),
                (52.3482, 4.9249, 80),
                (52.3382, 4.9393, 80),
                (52.3309, 4.9244, 80),
                (52.3288, 4.9165, 80),
                (52.3185, 4.9168, 80),
                (52.3110, 4.9244, 80),
                (52.2989, 4.9086, 70),
                (52.2976, 4.8944, 60),
                (52.3003, 4.8596, 25),
            ]),
            // Utrecht to Amersfoort.
            DemoTrips.Trip(today.AddDays(-25).AddHours(14), id: 9,
            [
                (52.0907, 5.1215, 25),
                (52.0931, 5.1367, 50),
                (52.0930, 5.1452, 70),
                (52.0917, 5.1621, 80),
                (52.0924, 5.1803, 90),
                (52.0931, 5.2011, 100),
                (52.1038, 5.2363, 100),
                (52.1093, 5.2733, 100),
                (52.1154, 5.3030, 90),
                (52.1229, 5.3404, 80),
                (52.1281, 5.3635, 70),
                (52.1561, 5.3878, 25),
            ]),
            // Tilburg to Breda.
            DemoTrips.Trip(today.AddDays(-35).AddHours(11), id: 10,
            [
                (51.5556, 5.0915, 25),
                (51.5380, 5.0634, 80),
                (51.5399, 5.0309, 90),
                (51.5383, 4.9994, 100),
                (51.5505, 4.9631, 100),
                (51.5528, 4.9313, 100),
                (51.5566, 4.9008, 100),
                (51.5578, 4.8635, 90),
                (51.5524, 4.8343, 80),
                (51.5604, 4.8206, 60),
                (51.5719, 4.7683, 25),
            ]),
            // In progress, Amsterdam to Duivendrecht: the road already driven, ending where the car
            // is now. It starts last, so it stays the final entry once DemoTripRepository orders the
            // list, and the frontend treats that entry as the active trip whenever
            // CurrentJourneyDistance > 0.
            DemoTrips.Trip(now.AddMinutes(-8), id: null,
            [
                (52.3676, 4.9041, 30),
                (52.3600, 4.9143, 45),
                (52.3520, 4.9210, 55),
                (52.3455, 4.9330, 60),
                (52.3401, 4.9455, 65),
            ]),
        ];
    }
}
