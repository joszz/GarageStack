import * as LModule from 'leaflet'
import type { LatLngBounds, Map as LeafletMap } from 'leaflet'
import markerIconUrl from 'leaflet/dist/images/marker-icon.png'
import markerIcon2xUrl from 'leaflet/dist/images/marker-icon-2x.png'
import markerShadowUrl from 'leaflet/dist/images/marker-shadow.png'

// Vite wraps CJS modules in a frozen ESM namespace - `import * as LModule` gives that frozen
// namespace. Plugins like leaflet.markercluster patch the actual mutable CJS
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
 * Pixels kept free around what a map frames: the same on every side, or [x, y] from the top-left
 * and bottom-right corners, to stay clear of a button or attribution laid over the map.
 */
export type FitPadding =
  number | { topLeft: readonly [number, number]; bottomRight: readonly [number, number] }

/**
 * Frames `bounds` without animating. A single point has no extent to fit, so the map centres on
 * it at street level instead of zooming in as far as it can go.
 */
export function fitToBounds(map: LeafletMap, bounds: LatLngBounds, padding: FitPadding): void {
  if (bounds.getNorthEast().equals(bounds.getSouthWest())) {
    map.setView(bounds.getCenter(), STREET_ZOOM, { animate: false })
  } else if (typeof padding === 'number') {
    map.fitBounds(bounds, { padding: [padding, padding], animate: false })
  } else {
    map.fitBounds(bounds, {
      paddingTopLeft: [...padding.topLeft],
      paddingBottomRight: [...padding.bottomRight],
      animate: false,
    })
  }
}

/**
 * `route` plus those of `extras` that lie near it: within half the route's own size of its bounds.
 * Framing a short trip together with a parking spot far away would shrink the trip to a few pixels.
 */
export function withNearby(
  route: readonly [number, number][],
  extras: readonly [number, number][],
): [number, number][] {
  if (route.length === 0) return [...extras]
  const near = L.latLngBounds([...route]).pad(0.5)
  return [...route, ...extras.filter((p) => near.contains(p))]
}

/** {@link fitToBounds} for a list of [lat, lng] points; no points leaves the map where it is. */
export function fitToPoints(
  map: LeafletMap,
  points: readonly [number, number][],
  padding: FitPadding,
): void {
  if (points.length === 0) return
  fitToBounds(map, L.latLngBounds([...points]), padding)
}

/**
 * The smallest bounds that hold every point of a trip with the car at their exact centre: the
 * largest offset from the car to any point, mirrored on the other side. Framing these keeps the
 * whole route in view as it grows, without the view drifting off the car.
 */
export function boundsAroundCar(
  car: readonly [number, number],
  points: readonly { latitude: number; longitude: number }[],
): LatLngBounds {
  let maxLatDelta = 0
  let maxLngDelta = 0
  for (const p of points) {
    maxLatDelta = Math.max(maxLatDelta, Math.abs(p.latitude - car[0]))
    maxLngDelta = Math.max(maxLngDelta, Math.abs(p.longitude - car[1]))
  }
  return L.latLngBounds(
    [car[0] - maxLatDelta, car[1] - maxLngDelta],
    [car[0] + maxLatDelta, car[1] + maxLngDelta],
  )
}

/** A box inside a map, as its distance in pixels from each edge of the map's container. */
export interface MapInset {
  top: number
  right: number
  bottom: number
  left: number
}

/**
 * Where `inner` lies inside `outer`, both as the page lays them out (getBoundingClientRect).
 * Both scroll together, so the answer holds however far the page is scrolled.
 */
export function insetWithin(outer: DOMRectReadOnly, inner: DOMRectReadOnly): MapInset {
  return {
    top: inner.top - outer.top,
    right: outer.right - inner.right,
    bottom: outer.bottom - inner.bottom,
    left: inner.left - outer.left,
  }
}

/**
 * Puts `point` at the middle of `box` at `zoom`, rather than at the middle of the map: the view's
 * centre is moved by as far as the box's middle lies from the map's.
 */
export function setViewInBox(
  map: LeafletMap,
  point: readonly [number, number],
  zoom: number,
  box: MapInset,
): void {
  const offset = L.point((box.left - box.right) / 2, (box.top - box.bottom) / 2)
  const centre = map.unproject(map.project([point[0], point[1]], zoom).subtract(offset), zoom)
  map.setView(centre, zoom, { animate: false })
}
