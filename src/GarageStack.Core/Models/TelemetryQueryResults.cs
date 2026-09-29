namespace GarageStack.Core.Models;

/// <summary>Distance and timestamp of the most recent journey the gateway reported.</summary>
public sealed record LastTripSummary(double DistanceKm, DateTime RecordedAt);

/// <summary>How often one raw MQTT topic started a telemetry row, and when it last did.</summary>
public sealed record RawTopicStat(string Topic, int Count, DateTime Last);

/// <summary>
/// One poll of the car's status (lock, doors, windows) and when it first reached GarageStack.
/// <see cref="State"/> is the telemetry row the poll landed in.
/// </summary>
public sealed record StatusReading(DateTime ArrivedAt, TelemetrySnapshot State);
