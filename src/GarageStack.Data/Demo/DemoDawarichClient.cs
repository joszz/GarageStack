using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

public sealed class DemoDawarichClient : IDawarichClient
{
    public Task<DawarichHealth> CheckAsync(CancellationToken ct) => Task.FromResult(DawarichHealth.Ok);

    public Task<IReadOnlyList<PhoneTracker>> GetTrackersAsync(DateTime since, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PhoneTracker>>([new("demo-phone", DateTimeOffset.UtcNow)]);

    public Task<IReadOnlyList<PhoneFix>> GetPointsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var fixes = new List<PhoneFix>();
        foreach (var trip in DemoTrips.All)
        {
            for (var i = 0; i < trip.Points.Count; i++)
            {
                var p = trip.Points[i];
                if (p.RecordedAt >= from && p.RecordedAt <= to)
                    fixes.Add(new(p.RecordedAt, p.Latitude, p.Longitude, 5, p.Speed, "demo-phone"));
                if (i == 0) continue;
                var before = trip.Points[i - 1];
                var at = before.RecordedAt + (p.RecordedAt - before.RecordedAt) / 2;
                if (at >= from && at <= to)
                    fixes.Add(new(at, (before.Latitude + p.Latitude) / 2, (before.Longitude + p.Longitude) / 2, 5, p.Speed, "demo-phone"));
            }
        }
        return Task.FromResult<IReadOnlyList<PhoneFix>>(fixes.OrderBy(f => f.RecordedAt).ToArray());
    }
}
