namespace GarageStack.Core.Configuration;

/// <summary>
/// The traction battery's real usable capacity in kWh, when the deployment configures one.
/// <para>
/// MG's API scales the BMS percentage by a nominal 72.5 kWh pack, and the MQTT gateway swaps in
/// the real size for drivetrain/soc_kwh and drivetrain/totalBatteryCapacity only on the BEV
/// series it knows. Everywhere else 72.5 comes through: wrong by a factor of forty on a plain
/// hybrid, where the buffer is under 2 kWh, and by three or more on a plug-in hybrid. Configuring
/// the real figure here overrides it for every client; left unset, a BEV keeps the gateway's
/// number and any other car shows state of charge as a percentage only.
/// </para>
/// </summary>
public record HvBatteryCapacity(double? Kwh)
{
    public static readonly HvBatteryCapacity Unknown = new(Kwh: null);
}
