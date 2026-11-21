using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

public sealed class DemoDawarichClient : IDawarichClient
{
    public Task<DawarichHealth> CheckAsync(CancellationToken ct) => Task.FromResult(DawarichHealth.Ok);

    public Task<IReadOnlyList<PhoneTracker>> GetTrackersAsync(DateTime since, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PhoneTracker>>(
            DemoTelemetryCapture.Snapshots is null ? [new("demo-phone", DateTimeOffset.UtcNow)] : []);

    public Task<IReadOnlyList<PhoneFix>> GetPointsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        if (DemoTelemetryCapture.Snapshots is not null)
            return Task.FromResult<IReadOnlyList<PhoneFix>>([]);

        var fixes = new List<PhoneFix>();
        foreach (var trip in DemoTrips.All)
        {
            for (var i = 0; i < trip.Points.Count; i++)
            {
                var point = trip.Points[i];
                if (point.RecordedAt >= from && point.RecordedAt <= to)
                    fixes.Add(new(point.RecordedAt, point.Latitude, point.Longitude, 5, point.Speed, "demo-phone"));
                if (i == 0) continue;

                var before = trip.Points[i - 1];
                var at = before.RecordedAt + (point.RecordedAt - before.RecordedAt) / 2;
                if (at >= from && at <= to)
                    fixes.Add(new(at, (before.Latitude + point.Latitude) / 2, (before.Longitude + point.Longitude) / 2, 5, point.Speed, "demo-phone"));
            }
        }
        return Task.FromResult<IReadOnlyList<PhoneFix>>(fixes.OrderBy(fix => fix.RecordedAt).ToArray());
    }
}
