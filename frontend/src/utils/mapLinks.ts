/** Coordinates are given to about 10 cm; more digits are noise from a consumer GPS. */
function round(value: number): string {
  return value.toFixed(6)
}

export function formatCoordinates(lat: number, lng: number): string {
  return `${round(lat)},${round(lng)}`
}

/** Universal directions links leave the origin to the map provider. */
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

/** openstreetmap.org with a marker on the point, which any browser can open. */
export function osmUrl(lat: number, lng: number, zoom = 17): string {
  const point = `mlat=${round(lat)}&mlon=${round(lng)}`
  return `https://www.openstreetmap.org/?${point}#map=${zoom}/${round(lat)}/${round(lng)}`
}
