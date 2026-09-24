import type { Place } from '@/services/mapApi'

/**
 * Turns a reverse-geocoded place into the strings the UI shows. Kept out of the components so
 * both the trip list and the location card read a place the same way, and so the rules stay
 * testable without mounting anything.
 */

function clean(value: string | null | undefined): string | null {
  const trimmed = value?.trim()
  return trimmed ? trimmed : null
}

/**
 * The settlement name. Falls back to the first segment of the geocoder's own label, but only for
 * a place with no street: that segment is the city on a city-level answer and the street (or
 * house number) on an address-level one.
 */
export function cityName(place: Place | null | undefined): string | null {
  if (!place) return null
  const city = clean(place.city)
  if (city) return city
  if (clean(place.road)) return null
  return clean(place.displayName?.split(',')[0])
}

/**
 * Street plus house number, in the order used across the app's locales ("Grote Markt 1"). A
 * place mapped only to a road keeps just the road.
 */
export function streetName(place: Place | null | undefined): string | null {
  if (!place) return null
  const road = clean(place.road)
  if (!road) return null
  const houseNumber = clean(place.houseNumber)
  return houseNumber ? `${road} ${houseNumber}` : road
}

/** Street and city on one line, thinning out to whatever is known: "Grote Markt 1, Zwolle". */
export function addressLabel(place: Place | null | undefined): string | null {
  if (!place) return null
  const parts = [streetName(place), cityName(place)].filter((part): part is string => part !== null)
  if (parts.length > 0) return parts.join(', ')
  return clean(place.displayName)
}
