namespace GarageStack.Worker.Mqtt;

/// <summary>
/// The reasons the gateway gives on drivetrain/chargingStopReason: the names of the SAIC client's
/// ChargingStopReason, or "UNKNOWN n" for a code it does not know. NO_REASON is never sent.
/// </summary>
public static class GatewayChargingStopReason
{
    /// <summary>
    /// The faults the car names. Its catch-all OTHER_REASON, and a code the gateway does not know,
    /// are not known to mean a fault, so a charge that stops with one of those still reads as
    /// complete: a false alarm on every charge would be worse than a missed one.
    /// </summary>
    public static readonly IReadOnlySet<string> Faults = new HashSet<string>(StringComparer.Ordinal)
    {
        "CHARGER_STATUS_ABNORMAL",
        "CHARGER_PORT_OVER_TEMPERATURE",
        "CHARGING_GUN_NOT_PROPERLY_PLUGGED_IN",
        "CHARGER_VOLTAGE_MISMATCH",
    };

    public static bool IsFault(string? reason) => reason is not null && Faults.Contains(reason);

    /// <summary>The notification string that words <paramref name="reason"/>, one per fault.</summary>
    public static string ResourceKey(string reason) => $"ChargingStopReason_{reason}";
}
