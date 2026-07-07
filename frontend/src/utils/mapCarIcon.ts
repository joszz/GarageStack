import { L } from '@/utils/leaflet'
import { CAR_SILHOUETTE_VIEWBOX, CAR_SILHOUETTE_MARKUP } from '@/assets/carSilhouette'

const CAR_ICON_SIZE: [number, number] = [24, 46]
const CAR_ICON_ANCHOR: [number, number] = [12, 23]

// Live car icon marking the vehicle's current position while a trip is in progress - used by
// both MapView (replacing the end-of-trip flag) and the dashboard's location preview. Rotated
// to the vehicle's current heading (0deg = north, matching the silhouette's forward-facing-up
// artwork). See .trip-marker--active / .trip-marker-car-svg in main.css for styling - kept
// there (not view-scoped CSS) so it renders correctly even when MapView's own chunk hasn't
// loaded yet.
export function buildCarMarkerIcon(headingDeg: number) {
  const heading = Number.isFinite(headingDeg) ? headingDeg : 0
  return L.divIcon({
    className: '',
    html: `<div class="trip-marker trip-marker--active" style="transform: rotate(${heading}deg)">
      <svg viewBox="${CAR_SILHOUETTE_VIEWBOX}" class="trip-marker-car-svg">${CAR_SILHOUETTE_MARKUP}</svg>
    </div>`,
    iconSize: CAR_ICON_SIZE,
    iconAnchor: CAR_ICON_ANCHOR,
  })
}
