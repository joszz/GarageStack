// Leaflet first: leaflet.heat extends the global L as it loads, and Leaflet is what sets that.
import { L } from '@/utils/leaflet'
import 'leaflet.heat'

/** A point on the heatmap, with an optional intensity. */
export type HeatPoint = [number, number] | [number, number, number]

export interface HeatLayerOptions {
  minOpacity?: number
  maxZoom?: number
  max?: number
  radius?: number
  blur?: number
  gradient?: Record<number, string>
}

type HeatLayerPrototype = { _initCanvas: (this: unknown) => void }

// What leaflet.heat adds to the shared Leaflet instance (see @/utils/leaflet for why it is shared).
const withHeat = L as typeof L & {
  heatLayer?: (points: HeatPoint[], options?: HeatLayerOptions) => L.Layer
  HeatLayer?: { prototype: HeatLayerPrototype }
}

const READS_FREQUENTLY = Symbol('readsFrequently')

/**
 * The heatmap is drawn by simpleheat, which colours it pixel by pixel: every redraw reads the whole
 * canvas back and writes it again, and leaflet.heat redraws after every pan and zoom. On a canvas
 * the browser keeps on the GPU, each of those reads is a slow round trip, and Chrome says so in the
 * console. A context asked for with `willReadFrequently` keeps the canvas in memory instead.
 *
 * A canvas keeps the options of the first context it hands out, and simpleheat asks for its own
 * the moment leaflet.heat has created the canvas, so there is no gap to ask first from outside.
 * Instead, while the layer sets up its canvas, every 2D context asked for is created for frequent
 * reads. Besides the heatmap canvas, that is only simpleheat's small helpers, which it reads back
 * once at most.
 */
function createCanvasesForFrequentReads(
  prototype: HeatLayerPrototype & { [READS_FREQUENTLY]?: true },
) {
  // Once only: a hot module reload evaluates this module again against the same prototype.
  if (prototype[READS_FREQUENTLY]) return
  prototype[READS_FREQUENTLY] = true

  const initCanvas = prototype._initCanvas
  prototype._initCanvas = function (this: unknown) {
    const getContext = HTMLCanvasElement.prototype.getContext
    HTMLCanvasElement.prototype.getContext = function (
      this: HTMLCanvasElement,
      contextId: string,
      options?: Record<string, unknown>,
    ) {
      return getContext.call(
        this,
        contextId,
        contextId === '2d' ? { ...options, willReadFrequently: true } : options,
      )
    } as typeof getContext
    try {
      initCanvas.call(this)
    } finally {
      HTMLCanvasElement.prototype.getContext = getContext
    }
  }
}

if (withHeat.HeatLayer) createCanvasesForFrequentReads(withHeat.HeatLayer.prototype)

/** A heatmap layer over `points`, or null when the plugin did not load. */
export function heatLayer(points: HeatPoint[], options?: HeatLayerOptions): L.Layer | null {
  return typeof withHeat.heatLayer === 'function' ? withHeat.heatLayer(points, options) : null
}
