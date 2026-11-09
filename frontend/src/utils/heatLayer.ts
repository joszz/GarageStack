import type { Layer } from 'leaflet'
import { HeatLayer } from 'leaflet.heat'

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

/**
 * A heatmap layer over `points`.
 *
 * simpleheat, which leaflet.heat draws with, reads the whole canvas back on every redraw, and it
 * asks for its context with `willReadFrequently` itself, so the browser keeps that canvas in memory
 * rather than on the GPU.
 */
export function heatLayer(points: HeatPoint[], options?: HeatLayerOptions): Layer {
  // leaflet.heat ships types for Leaflet 2, which do not line up with @types/leaflet 1.9; at run
  // time it extends the Leaflet 1.9 Layer this app uses.
  return new HeatLayer(points, options) as unknown as Layer
}
