namespace GarageStack.Core.Models;

public record TripPoint(DateTime RecordedAt, double Latitude, double Longitude, double? Speed);

public record TripDto(int Index, DateTime StartedAt, DateTime EndedAt, double DistanceKm, int PointCount, IReadOnlyList<TripPoint> Points);
