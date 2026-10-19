namespace GarageStack.Core.Models;

/// <summary>Where the car was by one GPS fix, and which way it faced.</summary>
public sealed record PositionFix(double Latitude, double Longitude, double? Heading);

/// <summary>
/// One poll of the car's status (lock, doors, windows) and when it first reached GarageStack.
/// <see cref="State"/> is the telemetry row the poll landed in.
/// </summary>
public sealed record StatusReading(DateTime ArrivedAt, TelemetrySnapshot State);
