import * as LModule from 'leaflet'
import type { LatLngBounds, Map as LeafletMap } from 'leaflet'
import markerIconUrl from 'leaflet/dist/images/marker-icon.png'
import markerIcon2xUrl from 'leaflet/dist/images/marker-icon-2x.png'
import markerShadowUrl from 'leaflet/dist/images/marker-shadow.png'

// Vite wraps CJS modules in a frozen ESM namespace - `import * as LModule` gives that frozen
// namespace. Plugins like leaflet.heat and leaflet.markercluster patch the actual mutable CJS
// export (LModule.default), so every consumer must resolve through this same shared instance
// rather than re-importing 'leaflet' directly, or plugin-added methods won't be visible on it.
export const L = ((LModule as unknown as { default?: typeof LModule }).default ??
  LModule) as typeof LModule

// Leaflet locates its default marker images through a stylesheet path a bundler cannot follow.
// vue-leaflet's <LMap> repairs that itself, but it fetches the three images with import() and
// waits for them before it creates the map, so as chunks of their own they were one more round
// trip in front of every map's first tiles (the dashboard's largest paint). Importing them here
// puts them in the chunk every map has already loaded, and that import() resolves at once.
delete (L.Icon.Default.prototype as { _getIconUrl?: unknown })._getIconUrl
L.Icon.Default.mergeOptions({
  iconUrl: markerIconUrl,
  iconRetinaUrl: markerIcon2xUrl,
  shadowUrl: markerShadowUrl,
})

export type { Map as LeafletMap } from 'leaflet'

const STREET_ZOOM = 15

/** Where a map opens before anything has placed it: Amsterdam, where the demo car drives. */
export const DEFAULT_MAP_CENTER: [number, number] = [52.3676, 4.9041]

/**
 * Frames `bounds` without animating. A single point has no extent to fit, so the map centres on
 * it at street level instead of zooming in as far as it can go.
 *
 * @param padding Pixels kept free around the bounds on every side.
 */
export function fitToBounds(map: LeafletMap, bounds: LatLngBounds, padding: number): void {
  if (bounds.getNorthEast().equals(bounds.getSouthWest())) {
    map.setView(bounds.getCenter(), STREET_ZOOM, { animate: false })
  } else {
    map.fitBounds(bounds, { padding: [padding, padding], animate: false })
  }
}

/** {@link fitToBounds} for a list of [lat, lng] points; no points leaves the map where it is. */
export function fitToPoints(
  map: LeafletMap,
  points: readonly [number, number][],
  padding: number,
): void {
  if (points.length === 0) return
  fitToBounds(map, L.latLngBounds([...points]), padding)
}
