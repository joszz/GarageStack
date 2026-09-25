using GarageStack.Api.Services;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class TripPlaceServiceTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private const string Nl = "nl";

    private static readonly (double Lat, double Lng) Zwolle = (52.5123, 6.0921);
    private static readonly (double Lat, double Lng) Deventer = (52.2554, 6.1639);

    private static readonly PlaceAddress ZwollePlace = new("Grote Markt 1, Zwolle", "Grote Markt", "1", "Zwolle", "8011 PK", "nl");
    private static readonly PlaceAddress DeventerPlace = new("Brink 2, Deventer", "Brink", "2", "Deventer", "7411 BT", "nl");

    private const string UnmappableResponse = """{"error":"Unable to geocode"}""";
    private const string ZwolleResponse = """
        {"display_name":"Grote Markt 1, Zwolle",
         "address":{"road":"Grote Markt","house_number":"1","city":"Zwolle","postcode":"8011 PK","country_code":"nl"}}
        """;

    private sealed class Setup : IAsyncDisposable
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        public Vehicle Vehicle { get; } = new() { Vin = "FAKEVN00000000001" };
        public TripRepository Trips { get; }
        public GeocodeFakeRepository GeocodeCache { get; } = new();
        public GeocodeFakeNominatimHandler Upstream { get; }
        public TripPlaceService Service { get; }

        public Setup(string upstreamBody = ZwolleResponse, bool geocodingEnabled = true)
        {
            Trips = new TripRepository(Db, new TelemetryRepository(Db));
            Upstream = new GeocodeFakeNominatimHandler(upstreamBody);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection([new("Geocoding:Enabled", geocodingEnabled ? "true" : "false")])
                .Build();
            var client = new NominatimApiClient(
                new PoiFakeHttpClientFactory(new HttpClient(Upstream)), config, NullLogger<NominatimApiClient>.Instance);
            Service = new TripPlaceService(Trips, new GeocodeService(GeocodeCache, client, NullLogger<GeocodeService>.Instance));
            Db.Vehicles.Add(Vehicle);
            Db.SaveChanges();
        }

        public async Task<long> SaveTripAsync(CancellationToken ct)
        {
            TripPoint[] points =
            [
                new(T0, Zwolle.Lat, Zwolle.Lng, 50),
                new(T0.AddMinutes(30), Deventer.Lat, Deventer.Lng, 50),
            ];
            var trip = new TripDto(0, T0, T0.AddMinutes(30), 30.0, points.Length, points);
            await Trips.SaveRecordedAsync(Vehicle.Id, [new RecordedTrip(trip, null, null)], T0.AddHours(1), ct);
            return (await Db.Trips.SingleAsync(ct)).Id;
        }

        public Task CacheAsync((double Lat, double Lng) at, PlaceAddress place, CancellationToken ct)
        {
            var (cellLat, cellLng) = GeocodePrecisionPolicy.CellOf(at.Lat, at.Lng, GeocodePrecisionPolicy.Address);
            return GeocodeCache.UpsertAsync(GeocodePrecisionPolicy.Address, Nl, cellLat, cellLng, place, TimeSpan.FromDays(1), ct);
        }

        public async Task<TripLogEntry> StoredAsync(long id, CancellationToken ct) =>
            Assert.Single(await Trips.GetLogEntriesAsync(Vehicle.Id, [id], ct));

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [Fact]
    public async Task MissingEnds_AreLookedUpAndKeptOnTheTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        var id = await s.SaveTripAsync(ct);
        await s.CacheAsync(Zwolle, ZwollePlace, ct);
        await s.CacheAsync(Deventer, DeventerPlace, ct);

        var result = await s.Service.ResolveAsync(s.Vehicle.Id, [id], Nl, ct);

        Assert.True(result.Available);
        Assert.False(result.HasMore);
        var answered = Assert.Single(result.Trips);
        Assert.Equal((ZwollePlace, DeventerPlace), (answered.StartPlace, answered.EndPlace));
        var stored = await s.StoredAsync(id, ct);
        Assert.Equal((ZwollePlace, DeventerPlace), (stored.StartPlace, stored.EndPlace));
    }

    [Fact]
    public async Task KnownEnds_AreNotLookedUpAgain_EvenOnceTheGeocodeCacheHasForgottenThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        var id = await s.SaveTripAsync(ct);
        await s.Trips.SavePlacesAsync(s.Vehicle.Id, [new TripPlaces(id, ZwollePlace, DeventerPlace)], ct);

        var result = await s.Service.ResolveAsync(s.Vehicle.Id, [id], Nl, ct);

        Assert.Equal(0, s.Upstream.CallCount);
        Assert.Equal(0, s.GeocodeCache.UpsertCallCount);
        var answered = Assert.Single(result.Trips);
        Assert.Equal((ZwollePlace, DeventerPlace), (answered.StartPlace, answered.EndPlace));
    }

    [Fact]
    public async Task UpstreamLookup_FillsTheEndTheCacheDidNotKnow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup(ZwolleResponse);
        var id = await s.SaveTripAsync(ct);
        await s.CacheAsync(Deventer, DeventerPlace, ct);

        await s.Service.ResolveAsync(s.Vehicle.Id, [id], Nl, ct);

        Assert.Equal(1, s.Upstream.CallCount);
        var stored = await s.StoredAsync(id, ct);
        Assert.Equal("Grote Markt", stored.StartPlace!.Road);
        Assert.Equal(DeventerPlace, stored.EndPlace);
    }

    [Fact]
    public async Task NothingMappedThere_IsAnsweredButNotKept()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup(UnmappableResponse);
        var id = await s.SaveTripAsync(ct);
        await s.CacheAsync(Deventer, DeventerPlace, ct);

        var result = await s.Service.ResolveAsync(s.Vehicle.Id, [id], Nl, ct);

        var answered = Assert.Single(result.Trips);
        Assert.True(answered.StartPlace!.IsEmpty);
        var stored = await s.StoredAsync(id, ct);
        Assert.Null(stored.StartPlace);
        Assert.Equal(DeventerPlace, stored.EndPlace);
    }

    [Fact]
    public async Task GeocodingSwitchedOff_SaysSo_AndStoresNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup(geocodingEnabled: false);
        var id = await s.SaveTripAsync(ct);

        var result = await s.Service.ResolveAsync(s.Vehicle.Id, [id], Nl, ct);

        Assert.False(result.Available);
        Assert.False(result.HasMore);
        Assert.Equal(0, s.Upstream.CallCount);
        Assert.Null((await s.StoredAsync(id, ct)).StartPlace);
    }

    [Fact]
    public async Task AnotherVehiclesTrips_AreNotAnswered()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = new Setup();
        var id = await s.SaveTripAsync(ct);
        await s.CacheAsync(Zwolle, ZwollePlace, ct);
        await s.CacheAsync(Deventer, DeventerPlace, ct);

        var result = await s.Service.ResolveAsync(s.Vehicle.Id + 1, [id], Nl, ct);

        Assert.Empty(result.Trips);
        Assert.Null((await s.StoredAsync(id, ct)).StartPlace);
    }
}
