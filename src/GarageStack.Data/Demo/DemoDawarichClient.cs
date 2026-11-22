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
        var fixes = DemoTrips.All
            .SelectMany(trip => DemoTrips.PhoneFixes(trip.Points, "demo-phone"))
            .Where(f => f.RecordedAt >= from && f.RecordedAt <= to);
        return Task.FromResult<IReadOnlyList<PhoneFix>>(fixes.OrderBy(f => f.RecordedAt).ToArray());
    }
}
