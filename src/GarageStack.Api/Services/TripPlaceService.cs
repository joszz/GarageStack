using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Services;

/// <summary>
/// Looks up the addresses at either end of trip log entries and keeps them on the trips. A trip
/// log is kept for a tax year, longer than the geocode cache holds a place, so an address found
/// once stays with its trip for good. The lookups go through <see cref="GeocodeService"/> and so
/// share its cache and its budget of a few upstream lookups per request: a long log fills in over
/// a few rounds, with the client asking again while <see cref="TripPlacesResult.HasMore"/> is set.
/// </summary>
public sealed class TripPlaceService(ITripRepository trips, GeocodeService geocoder)
{
    /// <summary>Trips one request may ask about: two ends each, within what one geocode batch accepts.</summary>
    public const int MaxTripsPerRequest = GeocodeDefaults.MaxPointsPerRequest / 2;

    public async Task<TripPlacesResult> ResolveAsync(
        int vehicleId, IReadOnlyCollection<long> ids, string language, CancellationToken ct = default)
    {
        var entries = await trips.GetLogEntriesAsync(vehicleId, ids, ct);

        // One point per end still missing, remembering which trip and end it answers for.
        var ends = new List<(int Entry, bool IsStart)>();
        var points = new List<GeoPoint>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.StartPlace is null)
            {
                ends.Add((i, true));
                points.Add(new GeoPoint(entry.StartLatitude, entry.StartLongitude));
            }
            if (entry.EndPlace is null)
            {
                ends.Add((i, false));
                points.Add(new GeoPoint(entry.EndLatitude, entry.EndLongitude));
            }
        }

        var known = entries.Select(e => new TripPlaces(e.Id, e.StartPlace, e.EndPlace)).ToArray();
        if (points.Count == 0) return new TripPlacesResult(known, Available: true, HasMore: false);

        var result = await geocoder.ResolveAsync(points, GeocodePrecisionPolicy.Address, language, ct);
        if (!result.Available) return new TripPlacesResult(known, Available: false, HasMore: false);

        // Newly found places are kept on the trip. "Nothing mapped here" is answered but not kept:
        // the geocoder only remembers that for a day, because OSM coverage grows, and a trip should
        // get the address once it exists rather than carry the blank for good.
        var found = new TripPlaces?[entries.Count];
        for (var j = 0; j < ends.Count; j++)
        {
            if (result.Results[j] is not { } dto) continue;
            var (i, isStart) = ends[j];
            var place = ToPlaceAddress(dto);

            known[i] = isStart ? known[i] with { StartPlace = place } : known[i] with { EndPlace = place };
            if (place.IsEmpty) continue;

            var kept = found[i] ?? new TripPlaces(known[i].Id, null, null);
            found[i] = isStart ? kept with { StartPlace = place } : kept with { EndPlace = place };
        }

        var toSave = found.OfType<TripPlaces>().ToList();
        if (toSave.Count > 0) await trips.SavePlacesAsync(vehicleId, toSave, ct);

        return new TripPlacesResult(known, Available: true, result.HasMore);
    }

    private static PlaceAddress ToPlaceAddress(PlaceDto p) =>
        new(p.DisplayName, p.Road, p.HouseNumber, p.City, p.Postcode, p.CountryCode);
}

/// <param name="Trips">The places now known for each requested trip; a place still null is not resolved yet.</param>
/// <param name="Available">False when geocoding is switched off for this deployment, so a client can stop asking.</param>
/// <param name="HasMore">True while some ends are still unresolved and a later request may fill them in.</param>
public sealed record TripPlacesResult(IReadOnlyList<TripPlaces> Trips, bool Available, bool HasMore);
