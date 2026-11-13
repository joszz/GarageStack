using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

/// <summary>
/// Rewrites a trip from a new route: a driver's edit or reset (TripRepository) and the phone's
/// points (PhoneFusionRepository). Changes the tracked entity only; the caller saves it.
/// </summary>
internal static class TripRouteUpdate
{
    // Everything read off a trip's points follows them. The odometer readings stay: the trip log
    // counts what the car's own odometer counted, whatever line is drawn on the map.
    public static async Task ApplyAsync(AppDbContext db, Trip trip, IReadOnlyList<TripPoint> points, CancellationToken ct)
    {
        var dto = TripEdit.Summarize(points);
        if (TripEdit.AddressMoved(trip.StartLatitude, trip.StartLongitude, points[0])) trip.StartPlaceJson = null;
        if (TripEdit.AddressMoved(trip.EndLatitude, trip.EndLongitude, points[^1])) trip.EndPlaceJson = null;

        trip.StartedAt = dto.StartedAt;
        trip.EndedAt = dto.EndedAt;
        trip.DistanceKm = dto.DistanceKm;
        trip.PointCount = dto.PointCount;
        trip.PointsJson = JsonSerializer.Serialize(points);
        trip.StartLatitude = points[0].Latitude;
        trip.StartLongitude = points[0].Longitude;
        trip.EndLatitude = points[^1].Latitude;
        trip.EndLongitude = points[^1].Longitude;
        trip.MaxSpeedKmh = dto.MaxSpeedKmh;
        trip.AvgMovingSpeedKmh = dto.AvgMovingSpeedKmh;
        trip.MovingSpeedSamples = dto.MovingSpeedSamples;

        if (!trip.PurposeIsManual)
        {
            var places = await db.CommutePlaces.AsNoTracking().Where(p => p.VehicleId == trip.VehicleId).ToListAsync(ct);
            trip.Purpose = CommuteRule.AutomaticPurpose(
                trip.StartLatitude, trip.StartLongitude, trip.EndLatitude, trip.EndLongitude, places);
        }
    }
}
