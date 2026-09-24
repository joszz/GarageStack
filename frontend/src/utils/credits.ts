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
export const VECTOR_ATTRIBUTION =
  // OpenFreeMap asks for the tile host, the schema and the data, and says of the three that its
  // own name is the one you may leave out ("You do not need to display the OpenFreeMap part, but
  // it is nice if you do"). That is what makes the credits fit on one row on a phone, so it is
  // marked as the part the stylesheet drops there and nowhere else.
  `<span class="map-credit-optional">${link('https://openfreemap.org', 'OpenFreeMap')} &middot; </span>` +
  `${link('https://openmaptiles.org', '&copy; OpenMapTiles')} &middot; ${OSM_ATTRIBUTION}`

/** Charging stations, added to the map only while that layer is on. */
export const OCM_ATTRIBUTION = link('https://openchargemap.org', 'Open Charge Map')
