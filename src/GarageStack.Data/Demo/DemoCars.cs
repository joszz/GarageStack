namespace GarageStack.Data.Demo;

/// <summary>
/// The demo's cars. The ZS EV is always there; the HS plug-in hybrid only when DEMO_SECOND_VEHICLE
/// is on (see <see cref="DemoOptions"/>). They differ in everything a dashboard shows: where they
/// are, their trips, their history, their battery and how they charge, so switching cars is plain
/// to see.
/// </summary>
internal static partial class DemoCars
{
    // Read when asked, not stored: the cars are defined in the other parts of this class, whose
    // fields are not set up in any order this one could count on.
    public static IReadOnlyList<DemoCar> All => [ZsEv, HsPhev];

    /// <summary>The demo car behind a vehicle id. Only the demo's own cars reach the repositories; anything else reads as the first.</summary>
    public static DemoCar For(int vehicleId) => All.FirstOrDefault(c => c.Vehicle.Id == vehicleId) ?? ZsEv;

    /// <summary>The demo car with this VIN, or the first car for any other.</summary>
    public static DemoCar ForVin(string vin) => All.FirstOrDefault(c => c.Vehicle.Vin == vin) ?? ZsEv;
}
