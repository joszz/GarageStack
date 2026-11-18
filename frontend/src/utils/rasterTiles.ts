// Apart from basemapStyle so the router can name the tile host without pulling the style code
// into the bundle every page loads.

/**
 * Used when WebGL is unavailable or the style cannot be fetched: the classic OSM raster tiles,
 * which need no WebGL context and no style document. Served from the plain host rather than the
 * deprecated {s}.tile.openstreetmap.org subdomain rotation, and capped at the zoom OSM renders.
 */
export const RASTER_FALLBACK_TILE_URL = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
export const RASTER_FALLBACK_MAX_ZOOM = 19
/** The raster tiles' host, for opening the connection before the first tile is asked for. */
export const RASTER_TILE_ORIGIN = new URL(RASTER_FALLBACK_TILE_URL).origin

/**
 * The service worker's cache of raster tiles this browser has shown, so the dashboard's map of
 * the parked car still has its streets without a connection. Only tiles that were on screen are
 * kept: OpenStreetMap's tile policy allows that, and forbids fetching areas ahead for offline use.
 */
export const MAP_TILE_CACHE = 'garagestack-map-tiles'
/** About 4 MB of tiles: the card's few tiles around many parking spots, and the map page's last views. */
export const MAP_TILE_CACHE_MAX_ENTRIES = 150
export const MAP_TILE_CACHE_MAX_AGE_SECONDS = 30 * 24 * 60 * 60

/**
 * Forgets the cached tiles, on sign-out: together they show where the car has been parked. A page
 * without Cache Storage (plain HTTP on a LAN address) has none to forget.
 */
export async function forgetMapTiles(): Promise<void> {
  try {
    await globalThis.caches?.delete(MAP_TILE_CACHE)
  } catch {
    // Nothing to do: the cache expires on its own.
  }
}
