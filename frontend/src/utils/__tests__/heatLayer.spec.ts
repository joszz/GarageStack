import { describe, it, expect, beforeEach, afterEach, vi, type MockInstance } from 'vitest'
import { L } from '@/utils/leaflet'
import { heatLayer } from '../heatLayer'

// jsdom has no canvas, so every 2D context is this stand-in: just enough for simpleheat to draw a
// heatmap into. What the tests look at is the options each context was asked for with.
const noop = () => {}
function fakeContext() {
  return {
    clearRect: noop,
    drawImage: noop,
    putImageData: noop,
    beginPath: noop,
    arc: noop,
    closePath: noop,
    fill: noop,
    fillRect: noop,
    getImageData: () => ({ data: new Uint8ClampedArray(1024) }),
    createLinearGradient: () => ({ addColorStop: noop }),
  }
}

type GetContext = (this: HTMLCanvasElement, contextId: string, options?: unknown) => unknown

describe('heatLayer', () => {
  let getContext: MockInstance<GetContext>
  let map: L.Map

  beforeEach(() => {
    getContext = vi
      .spyOn(HTMLCanvasElement.prototype, 'getContext')
      .mockImplementation(() => fakeContext() as never) as unknown as MockInstance<GetContext>
    map = L.map(document.createElement('div')).setView([52.5, 6.1], 12)
  })

  afterEach(() => {
    map.remove()
    vi.restoreAllMocks()
  })

  it('creates the heatmap canvas, which it reads back every redraw, for frequent reads', () => {
    heatLayer([[52.5, 6.1, 1]])!.addTo(map)

    const onHeatmapCanvas = getContext.mock.calls.filter((_, i) =>
      (getContext.mock.contexts[i] as HTMLCanvasElement).classList.contains(
        'leaflet-heatmap-layer',
      ),
    )
    expect(onHeatmapCanvas).toEqual([['2d', { willReadFrequently: true }]])
  })

  it('leaves every other canvas as it was', () => {
    heatLayer([[52.5, 6.1, 1]])!.addTo(map)
    getContext.mockClear()

    document.createElement('canvas').getContext('2d')

    expect(getContext).toHaveBeenCalledWith('2d')
  })
})
