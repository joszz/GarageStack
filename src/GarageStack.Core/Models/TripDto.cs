namespace GarageStack.Core.Models;

public record TripPoint(DateTime RecordedAt, double Latitude, double Longitude, double? Speed);

/// <summary>
/// A trip as the Api serves it. <paramref name="Id"/> is the saved trip's id, or null for a trip
/// the Worker has not saved yet, such as the one being driven.
/// </summary>
public record TripDto(int Index, DateTime StartedAt, DateTime EndedAt, double DistanceKm, int PointCount, IReadOnlyList<TripPoint> Points, long? Id = null);
