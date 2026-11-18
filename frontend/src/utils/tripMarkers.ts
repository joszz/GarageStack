import type { DivIcon } from 'leaflet'
import { L } from '@/utils/leaflet'

/**
 * The markers at either end of a trip, the same wherever a trip is drawn: a pulsing dot where it
 * started and a chequered flag where it ended. Their look is in `assets/tripMarkers.css`, which
 * every map that uses them imports.
 */

/** A 16 px dot centred on where the trip started. */
export function tripStartIcon(): DivIcon {
  return L.divIcon({
    className: '',
    html: '<div class="trip-marker trip-marker--start"></div>',
    iconSize: [16, 16],
    iconAnchor: [8, 8],
  })
}

/** A flag whose pole stands on where the trip ended, waving up and to the right of it. */
export function tripEndIcon(): DivIcon {
  return L.divIcon({
    className: '',
    html: '<div class="trip-marker trip-marker--end"><div class="trip-flag-pole"></div><div class="trip-flag-flag"></div></div>',
    iconSize: [20, 32],
    iconAnchor: [2, 32],
  })
}
