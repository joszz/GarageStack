import { describe, it, expect, vi } from 'vitest'
import { fitToPoints, withNearby, type LeafletMap } from '@/utils/leaflet'

function fakeMap() {
  return {
    setView: vi.fn<LeafletMap['setView']>(),
    fitBounds: vi.fn<LeafletMap['fitBounds']>(),
  }
}

describe('fitToPoints', () => {
  it('fits several points with the padding on every side', () => {
    const map = fakeMap()

    fitToPoints(
      map as unknown as LeafletMap,
      [
        [52.3, 4.8],
        [52.4, 4.9],
      ],
      24,
    )

    expect(map.fitBounds).toHaveBeenCalledWith(expect.anything(), {
      padding: [24, 24],
      animate: false,
    })
    expect(map.setView).not.toHaveBeenCalled()
  })

  it('centres on a single point at street level rather than zooming in all the way', () => {
    const map = fakeMap()

    fitToPoints(
      map as unknown as LeafletMap,
      [
        [52.3, 4.8],
        [52.3, 4.8],
      ],
      24,
    )

    expect(map.setView).toHaveBeenCalledWith(expect.objectContaining({ lat: 52.3, lng: 4.8 }), 15, {
      animate: false,
    })
    expect(map.fitBounds).not.toHaveBeenCalled()
  })

  it('leaves the map alone without points', () => {
    const map = fakeMap()

    fitToPoints(map as unknown as LeafletMap, [], 24)

    expect(map.setView).not.toHaveBeenCalled()
    expect(map.fitBounds).not.toHaveBeenCalled()
  })

  it('keeps more room free on the sides it is asked to', () => {
    const map = fakeMap()

    fitToPoints(
      map as unknown as LeafletMap,
      [
        [52.3, 4.8],
        [52.4, 4.9],
      ],
      { topLeft: [28, 56], bottomRight: [28, 32] },
    )

    expect(map.fitBounds).toHaveBeenCalledWith(expect.anything(), {
      paddingTopLeft: [28, 56],
      paddingBottomRight: [28, 32],
      animate: false,
    })
  })
})

describe('withNearby', () => {
  const route: [number, number][] = [
    [52.3, 4.86],
    [52.37, 4.94],
  ]

  it('adds a point near the route, and leaves out one far away', () => {
    const near: [number, number] = [52.29, 4.85]
    const far: [number, number] = [52.16, 5.39]

    expect(withNearby(route, [near, far])).toEqual([...route, near])
  })

  it('keeps the extras when there is no route to measure against', () => {
    expect(withNearby([], [[52.16, 5.39]])).toEqual([[52.16, 5.39]])
  })
})
