export {}

declare module 'leaflet' {
  interface HeatMapOptions {
    minOpacity?: number
    maxZoom?: number
    max?: number
    radius?: number
    blur?: number
    gradient?: Record<number, string>
  }

  function heatLayer(
    latlngs: Array<[number, number] | [number, number, number]>,
    options?: HeatMapOptions,
  ): Layer
}
