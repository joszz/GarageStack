namespace GarageStack.Core.Helpers;

/// <summary>
/// Whether a charge is AC or DC, from drivetrain/chargingType. The gateway publishes the SAIC API's
/// raw number there, which the API does not document. The gateway's README lists 6 as AC charging
/// (onboard charger input current present) and 7 as DC charging (high battery current, no AC
/// input), both checked against logged responses. The other values describe the car's state, such
/// as 10 for a charge that has ended, not the kind of charge. The demo writes "AC" and "DC".
/// </summary>
public static class ChargingKind
{
    public const string Ac = "AC";
    public const string Dc = "DC";

    /// <summary>"AC", "DC", or null when the value says neither.</summary>
    public static string? FromGateway(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "6" or Ac => Ac,
        "7" or Dc => Dc,
        _ => null,
    };
}
