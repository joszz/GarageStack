// The basemap styles' addresses, apart from basemapStyle so the service worker (no DOM, no path
// aliases) can import them to know which hosts' answers to keep.

// An env var set to an empty string means "not configured", not "use an empty URL".
function configured(value: string | undefined, fallback: string): string {
  const trimmed = value?.trim()
  return trimmed ? trimmed : fallback
}

/**
 * Vector basemap styles, one per variant, so the map follows the UI instead of staying bright
 * while everything around it is dark, unless the user asked for colour. OpenFreeMap serves
 * OpenStreetMap-derived vector tiles for free without an API key and can be self-hosted; a
 * deployment pointing at its own tile server overrides these at build time (VITE_MAP_STYLE_DARK /
 * VITE_MAP_STYLE_LIGHT / VITE_MAP_STYLE_COLORFUL) and must add that host to connect-src and
 * img-src in nginx-security-headers.conf, or the browser blocks it.
 */
export const BASEMAP_STYLE_URLS: Record<'dark' | 'light' | 'colorful', string> = {
  dark: configured(
    import.meta.env.VITE_MAP_STYLE_DARK,
    'https://tiles.openfreemap.org/styles/dark',
  ),
  light: configured(
    import.meta.env.VITE_MAP_STYLE_LIGHT,
    'https://tiles.openfreemap.org/styles/positron',
  ),
  // Liberty is OpenFreeMap's take on the standard OpenStreetMap map: the same greens, blues and
  // road colours the raster tiles show, so a colourful vector map and a colourful raster card
  // look like the same map.
  colorful: configured(
    import.meta.env.VITE_MAP_STYLE_COLORFUL,
    'https://tiles.openfreemap.org/styles/liberty',
  ),
}
