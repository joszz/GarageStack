import { describe, it, expect, vi } from 'vitest'
import {
  L,
  boundsAroundCar,
  fitToPoints,
  insetWithin,
  setViewInBox,
  withNearby,
  type LeafletMap,
} from '@/utils/leaflet'

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

describe('boundsAroundCar', () => {
  it('holds every point of the trip with the car at the exact centre', () => {
    const bounds = boundsAroundCar(
      [52.0, 5.0],
      [
        { latitude: 52.1, longitude: 5.0 },
        { latitude: 52.0, longitude: 4.8 },
      ],
    )

    expect(bounds.getCenter().lat).toBeCloseTo(52.0)
    expect(bounds.getCenter().lng).toBeCloseTo(5.0)
    expect(bounds.getNorth()).toBeCloseTo(52.1)
    expect(bounds.getSouth()).toBeCloseTo(51.9)
    expect(bounds.getWest()).toBeCloseTo(4.8)
    expect(bounds.getEast()).toBeCloseTo(5.2)
  })

  it('is only the car itself without points', () => {
    const bounds = boundsAroundCar([52.0, 5.0], [])

    expect(bounds.getNorthEast().equals(bounds.getSouthWest())).toBe(true)
  })
})

describe('insetWithin', () => {
  it('measures the inner box from each edge of the outer one', () => {
    const outer = new DOMRect(100, -300, 1000, 2000)
    const inner = new DOMRect(700, 0, 300, 400)

    expect(insetWithin(outer, inner)).toEqual({ top: 300, right: 100, bottom: 1300, left: 600 })
  })
})

describe('setViewInBox', () => {
  // Projects as Leaflet's own web mercator does, so the arithmetic is the real one.
  function projectingMap() {
    const crs = L.CRS.EPSG3857
    return {
      project: (latLng: [number, number], zoom: number) =>
        crs.latLngToPoint(L.latLng(latLng), zoom),
      unproject: (point: L.Point, zoom: number) => crs.pointToLatLng(point, zoom),
      setView: vi.fn<(centre: L.LatLng, zoom: number, options: object) => void>(),
    }
  }

  it("moves the view's centre so the point lands in the middle of the box", () => {
    const map = projectingMap()
    // A box in the top right of the map: its middle lies 300 px right of and 200 px above the map's.
    const box = { top: 0, right: 0, bottom: 400, left: 600 }

    setViewInBox(map as unknown as LeafletMap, [52.37, 4.9], 15, box)

    const [centre, zoom, options] = map.setView.mock.calls[0]!
    expect(zoom).toBe(15)
    expect(options).toEqual({ animate: false })
    const point = map.project([52.37, 4.9], 15)
    const middle = map.project([centre.lat, centre.lng], 15)
    expect(point.x - middle.x).toBeCloseTo(300)
    expect(point.y - middle.y).toBeCloseTo(-200)
  })

  it('centres on the point itself when the box is the middle of the map', () => {
    const map = projectingMap()

    setViewInBox(map as unknown as LeafletMap, [52.37, 4.9], 15, {
      top: 100,
      right: 50,
      bottom: 100,
      left: 50,
    })

    const [centre] = map.setView.mock.calls[0]!
    expect(centre.lat).toBeCloseTo(52.37)
    expect(centre.lng).toBeCloseTo(4.9)
  })
})
