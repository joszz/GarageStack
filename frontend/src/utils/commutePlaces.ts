import type { CommutePlace, CommutePlaceKind } from '@/services/commutePlacesApi'
import { distanceKm } from '@/utils/geo'

export const COMMUTE_PLACE_ICONS: Record<CommutePlaceKind, string> = {
  home: 'house',
  work: 'building',
}

/**
 * The place a trip end belongs to, by the same rule the API labels commutes with: within the
 * place's radius. The nearest one when places overlap.
 */
export function commutePlaceAt(
  places: readonly CommutePlace[],
  latitude: number,
  longitude: number,
): CommutePlace | null {
  let nearest: CommutePlace | null = null
  let nearestMeters = Infinity
  for (const place of places) {
    const meters = distanceKm([place.latitude, place.longitude], [latitude, longitude]) * 1000
    if (meters <= place.radiusMeters && meters < nearestMeters) {
      nearest = place
      nearestMeters = meters
    }
  }
  return nearest
}
