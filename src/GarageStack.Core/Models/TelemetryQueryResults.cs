namespace GarageStack.Core.Models;

/// <summary>Distance and timestamp of the most recent journey the gateway reported.</summary>
public sealed record LastTripSummary(double DistanceKm, DateTime RecordedAt);

/// <summary>How often one raw MQTT topic started a telemetry row, and when it last did.</summary>
public sealed record RawTopicStat(string Topic, int Count, DateTime Last);
