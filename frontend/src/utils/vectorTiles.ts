// The service worker's cache of the vector basemap (its style, tiles, fonts and sprites), apart
// from basemapStyle so the sign-out code can name the cache without pulling the style code into
// the bundle every page loads.

/**
 * What the maps have shown of the vector basemap, kept so their streets are still there without
 * a connection. Only what was on screen, as with the raster tiles.
 */
export const BASEMAP_CACHE = 'garagestack-basemap'
/** Some 20 to 30 MB: the style, fonts and sprites, and the tiles of the last few map views. */
export const BASEMAP_CACHE_MAX_ENTRIES = 400
export const BASEMAP_CACHE_MAX_AGE_SECONDS = 30 * 24 * 60 * 60

/**
 * The hosts the basemap styles are served from, whose answers the worker keeps. A style on the
 * app's own host (a relative address, or one naming it) is left out: the worker would otherwise
 * keep everything the app fetches from itself.
 */
export function basemapOrigins(styleUrls: readonly string[], ownOrigin: string): Set<string> {
  const origins = new Set<string>()
  for (const styleUrl of styleUrls) {
    try {
      const { origin } = new URL(styleUrl)
      if (origin !== ownOrigin) origins.add(origin)
    } catch {
      // A relative address: the app's own host.
    }
  }
  return origins
}

/**
 * The map renderer (MapLibre, its tile worker and its styles), kept once a map has loaded it. Not
 * in the precache, which every install downloads, map or not; without it offline the basemap
 * falls back to raster tiles this browser never fetched. Hashed names, so a kept copy never goes
 * stale; the two newest builds' worth are kept.
 */
export const MAP_RENDERER_CACHE = 'garagestack-map-renderer'
export const MAP_RENDERER_MAX_ENTRIES = 6

/** Whether a path of the app is one of the map renderer's files (see vite.config.ts). */
export function isMapRendererPath(pathname: string): boolean {
  return pathname.startsWith('/assets/maplibre')
}
