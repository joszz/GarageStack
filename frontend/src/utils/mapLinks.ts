/**
 * Links that hand a coordinate to something that can navigate to it. The car's position is only
 * half useful inside the app: what you want standing in a car park is the walking directions your
 * phone can give, which means leaving for whatever map app the device has.
 *
 * Two shapes, because one link cannot serve both: `geo:` is the platform-neutral handover a phone
 * resolves to its installed map app (Organic Maps, OsmAnd, Google Maps, ...) and does nothing at
 * all on a desktop, where an openstreetmap.org URL is what a browser can actually open.
 */

/** Coordinates are given to about 10 cm; more digits are noise from a consumer GPS. */
function round(value: number): string {
  return value.toFixed(6)
}

/**
 * RFC 5870 geo URI, with the label in the `q` parameter Android reads so the pin arrives named.
 * The coordinates are repeated there because a bare `geo:lat,lng` plus a query is ignored by some
 * apps, while `q=lat,lng(label)` is understood by all of them.
 */
export function geoUri(lat: number, lng: number, label?: string): string {
  const point = `${round(lat)},${round(lng)}`
  const query = label ? `${point}(${encodeURIComponent(label)})` : point
  return `geo:${point}?q=${query}`
}

/** openstreetmap.org with a marker on the point, which any browser can open. */
export function osmUrl(lat: number, lng: number, zoom = 17): string {
  const point = `mlat=${round(lat)}&mlon=${round(lng)}`
  return `https://www.openstreetmap.org/?${point}#map=${zoom}/${round(lat)}/${round(lng)}`
}

/**
 * True for the devices where a `geo:` link leads somewhere: a coarse pointer (touch) is the signal
 * that separates phones and tablets from the desktops where the same link would do nothing. Read
 * through matchMedia rather than the user agent so it follows the device rather than a string that
 * lies.
 */
export function prefersMapApp(): boolean {
  return typeof window !== 'undefined' && window.matchMedia?.('(pointer: coarse)').matches === true
}

export interface MapLink {
  href: string
  /** A web page opens in its own tab; a handover to a native app must not leave a blank one behind. */
  external: boolean
}

/** The link to offer for a coordinate on this device. */
export function mapAppLink(lat: number, lng: number, label?: string, useMapApp?: boolean): MapLink {
  return (useMapApp ?? prefersMapApp())
    ? { href: geoUri(lat, lng, label), external: false }
    : { href: osmUrl(lat, lng), external: true }
}
