namespace GarageStack.Core.Configuration;

/// <summary>
/// The traction battery's real usable capacity in kWh, when the deployment configures one.
/// <para>
/// The MQTT gateway does not read capacity off the pack: it scales the BMS percentage by an
/// EV-sized default to produce drivetrain/soc_kwh and drivetrain/totalBatteryCapacity. That is
/// fine for a car that has such a pack and wrong by a factor of forty on a plain hybrid, where
/// the buffer is under 2 kWh but 72.5 is reported. Configuring the real figure here overrides it
/// for every client; left unset, a plug-in car keeps the gateway's number and a hybrid shows
/// state of charge as a percentage only.
/// </para>
/// </summary>
public record HvBatteryCapacity(double? Kwh)
{
    public static readonly HvBatteryCapacity Unknown = new(Kwh: null);
}
