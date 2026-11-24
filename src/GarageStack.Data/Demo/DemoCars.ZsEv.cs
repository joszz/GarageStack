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
        // A few of each purpose, a way back labelled as the way out, with the oldest trips left
        // unclassified so the demo shows every state a trip log line can be in.
        Purposes = new Dictionary<long, TripPurpose>
        {
            [1] = TripPurpose.Business,
            [21] = TripPurpose.Business,
            [2] = TripPurpose.Business,
            [18] = TripPurpose.Business,
            [3] = TripPurpose.Private,
            [4] = TripPurpose.Business,
            [5] = TripPurpose.Private,
            [16] = TripPurpose.Private,
            [6] = TripPurpose.Private,
            [19] = TripPurpose.Private,
            [20] = TripPurpose.Private,
            [7] = TripPurpose.Commute,
            [17] = TripPurpose.Private,
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
    // the demo starts. Only the trip in progress runs from now. Each trip leaves from where the one
    // before it ended, so the car never jumps across the map, with two exceptions the demo keeps to
    // show what the app makes of them: the way home from Utrecht, which no poll caught (a straight
    // line), and the way home from Amstelveen, missed altogether, so the Almere trip leaves 8 km from
    // where the trip before it ended and the route editor suggests where it really left from.
    private static List<TripDto> ZsEvTrips(DateTime now)
    {
        var today = now.Date;
        return
        [
            // A day out to Tilburg and Breda, a phone riding along on the way between them.
            DemoTrips.Trip(today.AddDays(-35).AddHours(7).AddMinutes(30), id: 12, ZsHomeToTilburg),
            DemoTrips.Trip(today.AddDays(-35).AddHours(11), id: 10, ZsTilburgToBreda),
            DemoTrips.Trip(today.AddDays(-35).AddHours(15).AddMinutes(30), id: 13, ZsBredaToHome),
            // Utrecht, on to Amersfoort, and home.
            DemoTrips.Trip(today.AddDays(-25).AddHours(9), id: 14, ZsAmsterdamToUtrecht),
            DemoTrips.Trip(today.AddDays(-25).AddHours(14), id: 9, ZsUtrechtToAmersfoort),
            DemoTrips.Trip(today.AddDays(-25).AddHours(17), id: 15, ZsAmersfoortToHome),
            // Amstelveen, and the way home that was never recorded.
            DemoTrips.Trip(today.AddDays(-20).AddHours(10), id: 8, ZsAmsterdamToAmstelveen),
            // Almere and back.
            DemoTrips.Trip(today.AddDays(-18).AddHours(16), id: 5, ZsAmsterdamToAlmere),
            DemoTrips.Trip(today.AddDays(-18).AddHours(20), id: 16, ZsAlmereToHome),
            // Utrecht with a phone riding along, and the way back, which no poll caught: the car
            // stood in Utrecht and was next seen at home.
            DemoTrips.Trip(today.AddDays(-12).AddHours(10), id: 4, ZsAmsterdamToUtrecht),
            DemoTrips.Unseen(today.AddDays(-12).AddHours(17), id: 11, (52.0907, 5.1215), (52.3676, 4.9041), TimeSpan.FromMinutes(45)),
            // A drive through town, then out to Zaandam for the evening and back the next morning.
            DemoTrips.Trip(today.AddDays(-8).AddHours(14), id: 3, ZsAroundTown),
            DemoTrips.Trip(today.AddDays(-8).AddHours(19).AddMinutes(30), id: 17, ZsHomeToZaandam),
            DemoTrips.Trip(today.AddDays(-7).AddHours(8), id: 7, ZsZaandamToAmsterdam),
            // Haarlem and back.
            DemoTrips.Trip(today.AddDays(-5).AddHours(9), id: 2, ZsAmsterdamToHaarlem),
            DemoTrips.Trip(today.AddDays(-5).AddHours(17).AddMinutes(30), id: 18, ZsHaarlemToHome),
            // Den Haag, on to Delft, and home.
            DemoTrips.Trip(today.AddDays(-3).AddHours(12), id: 19, ZsHomeToDenHaag),
            DemoTrips.Trip(today.AddDays(-3).AddHours(17), id: 6, ZsDenHaagToDelft),
            DemoTrips.Trip(today.AddDays(-3).AddHours(19).AddMinutes(30), id: 20, ZsDelftToHome),
            // Schiphol and back.
            DemoTrips.Trip(today.AddDays(-2).AddHours(8), id: 1, ZsHomeToSchiphol),
            DemoTrips.Trip(today.AddDays(-2).AddHours(9).AddMinutes(30), id: 21, ZsSchipholToHome),
            // In progress, from home towards Duivendrecht: the road already driven, ending where the
            // car is now. It starts last, so it stays the final entry once DemoTripRepository orders
            // the list, and the frontend treats that entry as the active trip whenever
            // CurrentJourneyDistance > 0.
            DemoTrips.Trip(now.AddMinutes(-8), id: null, ZsUnderWay),
        ];
    }

    // The routes, as latitude, longitude and speed in km/h, all sourced from OSRM road routing so
    // the coordinates follow real roads. The ones joining the trips up start and end exactly where
    // the trips either side do.

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsHomeToTilburg =
    [
        (52.3676, 4.9041, 30),
        (52.3516, 4.9137, 55),
        (52.3383, 4.9393, 95),
        (52.3275, 4.9103, 105),
        (52.2753, 4.9562, 110),
        (52.2236, 4.9851, 110),
        (52.1631, 4.9886, 110),
        (52.1299, 5.0125, 110),
        (52.1062, 5.0585, 110),
        (52.0878, 5.0746, 110),
        (52.0263, 5.0636, 110),
        (51.9913, 5.0813, 110),
        (51.9205, 5.1729, 110),
        (51.8251, 5.2582, 110),
        (51.7724, 5.2609, 110),
        (51.7167, 5.3368, 110),
        (51.7004, 5.3509, 110),
        (51.6783, 5.3440, 110),
        (51.6632, 5.2947, 85),
        (51.6116, 5.2016, 85),
        (51.5796, 5.1494, 110),
        (51.5458, 5.1163, 110),
        (51.5450, 5.1117, 45),
        (51.5562, 5.1053, 25),
        (51.5556, 5.0915, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsTilburgToBreda =
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
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsBredaToHome =
    [
        (51.5719, 4.7683, 30),
        (51.5817, 4.8137, 55),
        (51.5808, 4.8261, 115),
        (51.5926, 4.8294, 110),
        (51.6370, 4.8821, 110),
        (51.6730, 4.8799, 110),
        (51.7213, 4.8914, 110),
        (51.7489, 4.9219, 110),
        (51.8028, 4.9322, 110),
        (51.9323, 4.9968, 110),
        (51.9530, 5.0203, 110),
        (51.9569, 5.0736, 110),
        (51.9745, 5.1057, 110),
        (51.9888, 5.0834, 110),
        (52.0244, 5.0642, 110),
        (52.0893, 5.0745, 110),
        (52.1065, 5.0588, 110),
        (52.1300, 5.0131, 110),
        (52.1633, 4.9894, 110),
        (52.2238, 4.9860, 110),
        (52.2754, 4.9568, 110),
        (52.3232, 4.9146, 110),
        (52.3280, 4.9161, 105),
        (52.3364, 4.9436, 50),
        (52.3516, 4.9139, 40),
        (52.3676, 4.9041, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsAmsterdamToUtrecht =
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
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsUtrechtToAmersfoort =
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
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsAmersfoortToHome =
    [
        (52.1561, 5.3878, 30),
        (52.1626, 5.3706, 40),
        (52.1674, 5.3756, 45),
        (52.1754, 5.3686, 85),
        (52.2087, 5.3648, 90),
        (52.2090, 5.3676, 110),
        (52.2228, 5.2885, 110),
        (52.2244, 5.2581, 110),
        (52.2277, 5.2460, 110),
        (52.2464, 5.2160, 105),
        (52.2644, 5.2077, 110),
        (52.2771, 5.1862, 110),
        (52.2889, 5.1828, 110),
        (52.3036, 5.1673, 110),
        (52.3061, 5.1567, 110),
        (52.3062, 5.1320, 110),
        (52.3142, 5.1127, 110),
        (52.3203, 5.0866, 110),
        (52.3312, 5.0332, 110),
        (52.3337, 5.0000, 105),
        (52.3523, 4.9642, 95),
        (52.3403, 4.9518, 110),
        (52.3367, 4.9395, 85),
        (52.3482, 4.9251, 40),
        (52.3516, 4.9139, 40),
        (52.3676, 4.9041, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsAmsterdamToAmstelveen =
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
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsAmsterdamToAlmere =
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
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsAlmereToHome =
    [
        (52.3702, 5.2159, 30),
        (52.3751, 5.2154, 40),
        (52.3730, 5.2018, 40),
        (52.3679, 5.2041, 40),
        (52.3637, 5.2016, 40),
        (52.3607, 5.1939, 40),
        (52.3558, 5.1915, 40),
        (52.3513, 5.1952, 120),
        (52.3489, 5.1920, 110),
        (52.3370, 5.1617, 110),
        (52.3217, 5.1309, 110),
        (52.3153, 5.1091, 110),
        (52.3308, 5.0354, 110),
        (52.3339, 4.9993, 105),
        (52.3523, 4.9642, 95),
        (52.3403, 4.9518, 110),
        (52.3367, 4.9395, 85),
        (52.3482, 4.9251, 40),
        (52.3485, 4.9188, 40),
        (52.3516, 4.9139, 40),
        (52.3676, 4.9041, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsAroundTown =
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
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsHomeToZaandam =
    [
        (52.3676, 4.9041, 30),
        (52.3733, 4.9123, 55),
        (52.3847, 4.9115, 55),
        (52.3902, 4.9209, 55),
        (52.4088, 4.9408, 90),
        (52.4080, 4.9427, 85),
        (52.4087, 4.9349, 110),
        (52.4238, 4.9001, 90),
        (52.4258, 4.8776, 55),
        (52.4324, 4.8696, 45),
        (52.4266, 4.8275, 45),
        (52.4356, 4.8168, 20),
        (52.4379, 4.8250, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsZaandamToAmsterdam =
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
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsAmsterdamToHaarlem =
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
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsHaarlemToHome =
    [
        (52.3871, 4.6458, 30),
        (52.3847, 4.6493, 40),
        (52.3809, 4.6471, 55),
        (52.3837, 4.7067, 115),
        (52.3729, 4.7103, 110),
        (52.3493, 4.7526, 110),
        (52.3344, 4.7608, 110),
        (52.3282, 4.7722, 110),
        (52.3252, 4.7833, 110),
        (52.3283, 4.7949, 110),
        (52.3382, 4.8165, 100),
        (52.3371, 4.8442, 100),
        (52.3385, 4.8838, 110),
        (52.3295, 4.9048, 110),
        (52.3283, 4.9133, 105),
        (52.3360, 4.9430, 80),
        (52.3483, 4.9250, 40),
        (52.3485, 4.9188, 40),
        (52.3516, 4.9139, 40),
        (52.3676, 4.9041, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsHomeToDenHaag =
    [
        (52.3676, 4.9041, 30),
        (52.3517, 4.9136, 40),
        (52.3482, 4.9187, 55),
        (52.3480, 4.9253, 85),
        (52.3383, 4.9393, 120),
        (52.3355, 4.9376, 110),
        (52.3285, 4.9133, 110),
        (52.3396, 4.8844, 100),
        (52.3386, 4.8161, 110),
        (52.3280, 4.7924, 110),
        (52.3221, 4.7607, 110),
        (52.3167, 4.7530, 110),
        (52.2970, 4.7392, 110),
        (52.2214, 4.6313, 110),
        (52.1850, 4.5992, 110),
        (52.1724, 4.5697, 120),
        (52.1555, 4.5432, 110),
        (52.1444, 4.5142, 110),
        (52.0777, 4.4045, 105),
        (52.0634, 4.3745, 85),
        (52.0717, 4.3462, 60),
        (52.0779, 4.3356, 30),
        (52.0652, 4.3066, 40),
        (52.0707, 4.3008, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsDenHaagToDelft =
    [
        (52.0707, 4.3008, 20),
        (52.0674, 4.3034, 55),
        (52.0641, 4.3107, 80),
        (52.0505, 4.3137, 90),
        (52.0346, 4.3288, 75),
        (52.0291, 4.3372, 70),
        (52.0227, 4.3471, 55),
        (52.0117, 4.3573, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsDelftToHome =
    [
        (52.0117, 4.3573, 20),
        (52.0145, 4.3668, 40),
        (52.0103, 4.3704, 40),
        (52.0118, 4.3787, 110),
        (52.0311, 4.3552, 110),
        (52.0400, 4.3523, 110),
        (52.0651, 4.3792, 110),
        (52.1040, 4.4516, 110),
        (52.1445, 4.5152, 110),
        (52.1563, 4.5457, 110),
        (52.1718, 4.5692, 110),
        (52.1848, 4.5996, 105),
        (52.1990, 4.6155, 115),
        (52.2209, 4.6312, 110),
        (52.2979, 4.7408, 110),
        (52.3209, 4.7588, 110),
        (52.3274, 4.7922, 110),
        (52.3382, 4.8160, 100),
        (52.3385, 4.8838, 110),
        (52.3283, 4.9133, 105),
        (52.3362, 4.9434, 80),
        (52.3483, 4.9250, 40),
        (52.3516, 4.9139, 40),
        (52.3676, 4.9041, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsHomeToSchiphol =
    [
        (52.3676, 4.9041, 30),
        (52.3517, 4.9136, 40),
        (52.3482, 4.9187, 55),
        (52.3480, 4.9253, 85),
        (52.3383, 4.9393, 120),
        (52.3355, 4.9376, 110),
        (52.3285, 4.9133, 110),
        (52.3396, 4.8844, 100),
        (52.3385, 4.8146, 110),
        (52.3284, 4.7933, 110),
        (52.3279, 4.7784, 105),
        (52.3220, 4.7601, 60),
        (52.3076, 4.7449, 40),
        (52.3059, 4.7573, 30),
        (52.3090, 4.7635, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsSchipholToHome =
    [
        (52.3090, 4.7635, 30),
        (52.3093, 4.7495, 90),
        (52.3202, 4.7580, 110),
        (52.3236, 4.7666, 110),
        (52.3274, 4.7922, 110),
        (52.3380, 4.8146, 100),
        (52.3385, 4.8838, 110),
        (52.3295, 4.9048, 110),
        (52.3283, 4.9133, 105),
        (52.3360, 4.9430, 80),
        (52.3483, 4.9250, 40),
        (52.3485, 4.9188, 40),
        (52.3516, 4.9139, 40),
        (52.3676, 4.9041, 20),
    ];

    private static readonly (double Lat, double Lon, double SpeedKmh)[] ZsUnderWay =
    [
        (52.3676, 4.9041, 30),
        (52.3600, 4.9143, 45),
        (52.3520, 4.9210, 55),
        (52.3455, 4.9330, 60),
        (52.3401, 4.9455, 65),
    ];
}
