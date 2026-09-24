namespace GarageStack.Core.Models;

/// <summary>
/// One position on the map. A struct because a matched trip is thousands of these and they are
/// only ever read, never mutated or shared.
/// </summary>
public readonly record struct GeoCoordinate(double Lat, double Lng);
