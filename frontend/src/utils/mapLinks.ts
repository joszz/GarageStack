/**
 * Links that hand the car's position to something that can navigate to it. Every directions
 * link names only the destination: the map provider uses or asks for the starting point itself,
 * so GarageStack never needs the browser's location.
 */

/** Coordinates are given to about 10 cm; more digits are noise from a consumer GPS. */
export function formatCoordinates(lat: number, lng: number): string {
  return `${lat.toFixed(6)},${lng.toFixed(6)}`
}

/**
 * Walking directions on openstreetmap.org, the default. Its directions page reads the
 * destination from `to` and the routing engine from `engine`; FOSSGIS runs the OSRM foot router
 * behind it. The start stays empty, or the page's own location button fills it in.
 */
export function osmDirectionsUrl(lat: number, lng: number): string {
  const query = new URLSearchParams({
    engine: 'fossgis_osrm_foot',
    to: formatCoordinates(lat, lng),
  })
  return `https://www.openstreetmap.org/directions?${query}`
}

export function googleDirectionsUrl(lat: number, lng: number): string {
  const query = new URLSearchParams({
    api: '1',
    destination: formatCoordinates(lat, lng),
    travelmode: 'walking',
  })
  return `https://www.google.com/maps/dir/?${query}`
}

export function appleDirectionsUrl(lat: number, lng: number): string {
  const query = new URLSearchParams({
    destination: formatCoordinates(lat, lng),
    mode: 'walking',
  })
  return `https://maps.apple.com/directions?${query}`
}

/**
 * RFC 5870 geo URI, with the label in the `q` parameter Android reads so the pin arrives named.
 * The coordinates are repeated there because a bare `geo:lat,lng` plus a query is ignored by some
 * apps, while `q=lat,lng(label)` is understood by many compatible apps.
 */
export function geoUri(lat: number, lng: number, label?: string): string {
  const point = formatCoordinates(lat, lng)
  const query = label ? `${point}(${encodeURIComponent(label)})` : point
  return `geo:${point}?q=${query}`
}
