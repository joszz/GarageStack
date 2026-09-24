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
  paint?: Record<string, unknown>
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

/** WCAG AA for body text. Map labels are small, so anything under this is genuinely hard to read. */
const MIN_LABEL_CONTRAST = 4.5

// Text tones for a dark and a light basemap, mirroring the app's own --color-text in each theme
// so labels look like they belong to the interface rather than to the tile server.
const LABEL_ON_DARK = '#e2e8f0'
const LABEL_ON_LIGHT = '#1a202c'

interface Rgba {
  r: number
  g: number
  b: number
  a: number
}

/**
 * Lifts unreadable labels to a tone that clears WCAG AA against the basemap's own background, and
 * gives them a halo in that background colour so they stay legible over roads and parks.
 *
 * The dark style this ships with needs it badly: place names are rgb(101,101,101) on a
 * rgb(12,12,12) background (3.4:1) and water names are black with a dark grey halo, which is the
 * light style's styling left in place. The check is contrast-based rather than dark-only, so a
 * style whose labels already read well is returned untouched, whichever way round it is.
 */
export function boostLabelContrast(style: MapStyle): MapStyle {
  if (!Array.isArray(style.layers)) return style

  const background = parseColor(backgroundColorOf(style.layers))
  // Without a background colour there is nothing to measure against, and guessing would risk
  // turning a readable map unreadable.
  if (!background) return style

  const backgroundIsDark = relativeLuminance(background) < 0.5
  const target = backgroundIsDark ? LABEL_ON_DARK : LABEL_ON_LIGHT
  const halo = `rgba(${background.r}, ${background.g}, ${background.b}, 0.85)`

  return {
    ...style,
    layers: style.layers.map((layer) => {
      if (layer.type !== 'symbol' || layer.layout?.['text-field'] === undefined) return layer
      const paint = layer.paint ?? {}
      const declared = paint['text-color']
      // An expression means the colour varies with zoom or feature; rewriting it to one flat
      // value would throw away more than it fixes.
      if (declared !== undefined && typeof declared !== 'string') return layer
      // The style spec's default text-color is opaque black, which is why a label with no colour
      // of its own disappears on a dark basemap.
      const current = parseColor(declared ?? '#000000')
      if (!current || contrastAgainst(current, background) >= MIN_LABEL_CONTRAST) return layer

      const haloWidth = typeof paint['text-halo-width'] === 'number' ? paint['text-halo-width'] : 0
      return {
        ...layer,
        paint: {
          ...paint,
          'text-color': target,
          'text-halo-color': halo,
          'text-halo-width': Math.max(haloWidth, 1),
        },
      }
    }),
  }
}

function backgroundColorOf(layers: StyleLayer[]): string {
  const background = layers.find((layer) => layer.type === 'background')
  const color = background?.paint?.['background-color']
  return typeof color === 'string' ? color : ''
}

/** Contrast of a possibly translucent label against the background it is drawn on. */
function contrastAgainst(color: Rgba, background: Rgba): number {
  const blended = {
    r: color.a * color.r + (1 - color.a) * background.r,
    g: color.a * color.g + (1 - color.a) * background.g,
    b: color.a * color.b + (1 - color.a) * background.b,
    a: 1,
  }
  const light = Math.max(relativeLuminance(blended), relativeLuminance(background))
  const dark = Math.min(relativeLuminance(blended), relativeLuminance(background))
  return (light + 0.05) / (dark + 0.05)
}

function relativeLuminance({ r, g, b }: Rgba): number {
  const channel = (value: number) => {
    const c = value / 255
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
}

/** Handles the colour notations a MapLibre style may use: hex, rgb(a) and hsl(a). */
function parseColor(value: string): Rgba | null {
  const input = value.trim().toLowerCase()

  const hex = /^#([\da-f]{3,8})$/.exec(input)
  if (hex) {
    const digits = hex[1]!
    const short = digits.length <= 4
    const size = short ? 1 : 2
    const part = (index: number) => {
      const slice = digits.slice(index * size, index * size + size)
      if (slice.length < size) return null
      const parsed = Number.parseInt(short ? slice + slice : slice, 16)
      return Number.isNaN(parsed) ? null : parsed
    }
    const [r, g, b] = [part(0), part(1), part(2)]
    if (r === null || g === null || b === null) return null
    const alpha = part(3)
    return { r, g, b, a: alpha === null ? 1 : alpha / 255 }
  }

  const parts = /^(rgba?|hsla?)\(([^)]+)\)$/.exec(input)
  if (!parts) return null
  const numbers = parts[2]!
    .split(/[\s,/]+/)
    .filter(Boolean)
    .map((token) => Number.parseFloat(token))
  if (numbers.some(Number.isNaN) || numbers.length < 3) return null
  const alpha = numbers.length > 3 ? clamp(numbers[3]!, 0, 1) : 1

  if (parts[1]!.startsWith('rgb')) {
    return { r: numbers[0]!, g: numbers[1]!, b: numbers[2]!, a: alpha }
  }
  return { ...hslToRgb(numbers[0]!, numbers[1]! / 100, numbers[2]! / 100), a: alpha }
}

function hslToRgb(hue: number, saturation: number, lightness: number): Omit<Rgba, 'a'> {
  const c = (1 - Math.abs(2 * lightness - 1)) * saturation
  const h = (((hue % 360) + 360) % 360) / 60
  const x = c * (1 - Math.abs((h % 2) - 1))
  const [r, g, b] = (
    [
      [c, x, 0],
      [x, c, 0],
      [0, c, x],
      [0, x, c],
      [x, 0, c],
      [c, 0, x],
    ] as const
  )[Math.floor(h) % 6]!
  const m = lightness - c / 2
  return {
    r: Math.round((r + m) * 255),
    g: Math.round((g + m) * 255),
    b: Math.round((b + m) * 255),
  }
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
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
