import type { Locale, Theme } from '@/stores/settingsShared'

/**
 * A MapLibre style document, typed only as far as this module touches it. The full
 * StyleSpecification comes from maplibre-gl, which is loaded lazily - keeping the shape
 * structural here means the style can be fetched and localized without pulling that bundle in.
 */
export interface StyleLayer {
  id: string
  type: string
  layout?: Record<string, unknown>
  [key: string]: unknown
}

export interface MapStyle {
  layers?: StyleLayer[]
  [key: string]: unknown
}

// An env var set to an empty string means "not configured", not "use an empty URL".
function configured(value: string | undefined, fallback: string): string {
  const trimmed = value?.trim()
  return trimmed ? trimmed : fallback
}

/**
 * Vector basemap styles, one per app theme, so the map follows the UI instead of staying bright
 * while everything around it is dark. OpenFreeMap serves OpenStreetMap-derived vector tiles for
 * free without an API key and can be self-hosted; a deployment pointing at its own tile server
 * overrides these at build time (VITE_MAP_STYLE_DARK / VITE_MAP_STYLE_LIGHT) and must add that
 * host to connect-src and img-src in nginx-security-headers.conf, or the browser blocks it.
 */
export const BASEMAP_STYLE_URLS: Record<Theme, string> = {
  dark: configured(
    import.meta.env.VITE_MAP_STYLE_DARK,
    'https://tiles.openfreemap.org/styles/dark',
  ),
  light: configured(
    import.meta.env.VITE_MAP_STYLE_LIGHT,
    'https://tiles.openfreemap.org/styles/positron',
  ),
}

/**
 * Used when WebGL is unavailable or the style cannot be fetched: the classic OSM raster tiles,
 * which need no WebGL context and no style document. Served from the plain host rather than the
 * deprecated {s}.tile.openstreetmap.org subdomain rotation, and capped at the zoom OSM renders.
 */
export const RASTER_FALLBACK_TILE_URL = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
export const RASTER_FALLBACK_MAX_ZOOM = 19

/** ODbL requires the credit to link to the licence, which the old plain-text credit did not. */
export const OSM_ATTRIBUTION =
  '<a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">&copy; OpenStreetMap</a> contributors'

/**
 * Rewrites label layers to prefer the name in the UI language, falling back to whatever the
 * style already asked for (which is the local name for most of the world). OpenStreetMap carries
 * `name:<language>` tags per feature, so this is the one thing a vector basemap can do that a
 * pre-rendered raster tile cannot: the same map in Dutch or English without a second tile server.
 *
 * Only expression-valued text-fields are touched. The legacy string form ("{name}") cannot hold
 * a nested expression, and shield layers that label a road by its `ref` have no name to localize.
 */
export function localizeStyleLabels(style: MapStyle, locale: Locale): MapStyle {
  if (!Array.isArray(style.layers)) return style

  const localName = `name:${locale}`
  return {
    ...style,
    layers: style.layers.map((layer) => {
      const textField = layer.layout?.['text-field']
      if (!Array.isArray(textField) || !mentionsName(textField)) return layer
      return {
        ...layer,
        layout: {
          ...layer.layout,
          'text-field': ['coalesce', ['get', localName], textField],
        },
      }
    }),
  }
}

// Every OpenMapTiles label layer reads some flavour of the name property (name, name:latin,
// name_en); a layer that reads none of them labels something else entirely and is left alone.
function mentionsName(textField: unknown[]): boolean {
  return JSON.stringify(textField).includes('"name')
}

// A WebGL context is expensive to create, so the answer is kept for the session: it cannot
// change while the page is open.
let webGlSupport: boolean | null = null

/**
 * Whether this browser can render the vector basemap at all. False in jsdom (so unit tests take
 * the raster path without loading MapLibre), on the odd locked-down browser, and when the GPU
 * process has crashed.
 */
export function supportsWebGl(): boolean {
  if (webGlSupport !== null) return webGlSupport
  try {
    const canvas = document.createElement('canvas')
    webGlSupport = Boolean(canvas.getContext('webgl2') ?? canvas.getContext('webgl'))
  } catch {
    webGlSupport = false
  }
  return webGlSupport
}
