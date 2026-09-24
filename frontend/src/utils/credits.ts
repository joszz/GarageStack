/**
 * The data credits the maps carry. Every source here asks for attribution as a condition of use:
 * OpenStreetMap under ODbL, Open Charge Map under CC BY, and OpenFreeMap in its own terms
 * ("OpenFreeMap © OpenMapTiles Data from OpenStreetMap"). All of them want the credit to name the
 * source and link to it, so these are links rather than plain text, and they live here rather than
 * next to whichever layer happens to need one so no layer ships without its credit.
 */

const LINK_ATTRS = 'target="_blank" rel="noreferrer"'

function link(href: string, text: string): string {
  return `<a href="${href}" ${LINK_ATTRS}>${text}</a>`
}

/** OpenStreetMap itself: the raster fallback tiles, and the data under every other credit here. */
export const OSM_ATTRIBUTION = `${link('https://www.openstreetmap.org/copyright', '&copy; OpenStreetMap')} contributors`

/**
 * The vector basemap. OpenFreeMap's styles declare no attribution on their sources, so
 * maplibre-gl-leaflet finds nothing to copy into Leaflet's control and the map would otherwise
 * carry no credit at all; this is handed to the layer explicitly instead.
 */
export const VECTOR_ATTRIBUTION = [
  link('https://openfreemap.org', 'OpenFreeMap'),
  link('https://openmaptiles.org', '&copy; OpenMapTiles'),
  OSM_ATTRIBUTION,
].join(' &middot; ')

/** Charging stations, added to the map only while that layer is on. */
export const OCM_ATTRIBUTION = link('https://openchargemap.org', 'Open Charge Map')
