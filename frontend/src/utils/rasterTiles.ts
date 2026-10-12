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
