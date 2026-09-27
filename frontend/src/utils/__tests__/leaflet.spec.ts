import { describe, it, expect, vi } from 'vitest'
import { fitToPoints, type LeafletMap } from '@/utils/leaflet'

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
})
