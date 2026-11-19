using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

internal static class DemoTrips
{
    public static IReadOnlyList<TripDto> All =>
        DemoTelemetryCapture.Snapshots is null ? DemoMockTrips.All : DemoCapturedTrips.All;

    public static IReadOnlyList<TripLogEntry> Log =>
        DemoTelemetryCapture.Snapshots is null ? DemoMockTrips.Log : DemoCapturedTrips.Log;
}
