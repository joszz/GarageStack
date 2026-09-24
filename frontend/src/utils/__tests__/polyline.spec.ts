import { describe, it, expect } from 'vitest'
import { decodePolyline } from '@/utils/polyline'

describe('decodePolyline', () => {
  // The example from the format's own documentation, at five decimals: the decoder is pinned to a
  // vector produced elsewhere rather than to its own output.
  const googleExample = '_p~iF~ps|U_ulLnnqC_mqNvxq`@'

  it('decodes a known encoding', () => {
    const decoded = decodePolyline(googleExample, 5)

    expect(decoded).toHaveLength(3)
    expect(decoded[0]![0]).toBeCloseTo(38.5, 5)
    expect(decoded[0]![1]).toBeCloseTo(-120.2, 5)
    expect(decoded[1]![0]).toBeCloseTo(40.7, 5)
    expect(decoded[1]![1]).toBeCloseTo(-120.95, 5)
    expect(decoded[2]![0]).toBeCloseTo(43.252, 5)
    expect(decoded[2]![1]).toBeCloseTo(-126.453, 5)
  })

  it('decodes the six decimals the map API encodes at', () => {
    // A snapped line through Zwolle, as the API would answer it.
    const decoded = decodePolyline('qdbdcBqbzrJ_ibE_ibE')

    expect(decoded).toHaveLength(2)
    expect(decoded[0]![0]).toBeCloseTo(52.512345, 6)
    expect(decoded[0]![1]).toBeCloseTo(6.092345, 6)
    expect(decoded[1]![0]).toBeCloseTo(52.612345, 6)
    expect(decoded[1]![1]).toBeCloseTo(6.192345, 6)
  })

  it('keeps the complete vertices of a truncated string', () => {
    const decoded = decodePolyline(googleExample.slice(0, -3), 5)

    expect(decoded.length).toBeGreaterThanOrEqual(1)
    expect(decoded.length).toBeLessThan(3)
    expect(decoded[0]![0]).toBeCloseTo(38.5, 5)
  })

  it.each([null, ''])('decodes %s to nothing', (encoded) => {
    expect(decodePolyline(encoded)).toEqual([])
  })
})
