import { describe, it, expect } from 'vitest'
import { distanceKm, polylineLengthKm, resampleByDistance, type LatLng } from '@/utils/geo'

// A degree of latitude is about 111.19 km anywhere, which makes north-south lines easy to reason
// about: a line from 52.0 to 52.1 is roughly 11.1 km long.
const KM_PER_DEGREE_LAT = 111.19

function northSouthLine(fromLat: number, toLat: number, steps: number, lng = 5): LatLng[] {
  const line: LatLng[] = []
  for (let i = 0; i <= steps; i++) {
    line.push([fromLat + ((toLat - fromLat) * i) / steps, lng])
  }
  return line
}

describe('distanceKm', () => {
  it('measures a degree of latitude', () => {
    expect(distanceKm([52, 5], [53, 5])).toBeCloseTo(KM_PER_DEGREE_LAT, 1)
  })

  it('is zero between identical coordinates', () => {
    expect(distanceKm([52.1, 5.3], [52.1, 5.3])).toBe(0)
  })

  it('shrinks a degree of longitude towards the pole', () => {
    const atEquator = distanceKm([0, 0], [0, 1])
    const atFiftyTwo = distanceKm([52, 0], [52, 1])
    expect(atFiftyTwo).toBeLessThan(atEquator)
    expect(atFiftyTwo).toBeCloseTo(atEquator * Math.cos((52 * Math.PI) / 180), 1)
  })
})

describe('polylineLengthKm', () => {
  it('sums the segments of a line', () => {
    const line = northSouthLine(52, 52.1, 10)
    expect(polylineLengthKm(line, 5)).toBeCloseTo(KM_PER_DEGREE_LAT * 0.1, 1)
  })

  it('leaves out jumps longer than the gap allowance', () => {
    // 2 km driven, then a 55 km jump while the car reported nothing, then 2 km driven again:
    // only the two stretches that were recorded count towards the length.
    const line: LatLng[] = [
      [52, 5],
      [52.018, 5],
      [52.518, 5],
      [52.536, 5],
    ]
    expect(polylineLengthKm(line, 5)).toBeCloseTo(KM_PER_DEGREE_LAT * 0.036, 1)
  })

  it('is zero for a line with nothing to measure', () => {
    expect(polylineLengthKm([], 5)).toBe(0)
    expect(polylineLengthKm([[52, 5]], 5)).toBe(0)
  })
})

describe('resampleByDistance', () => {
  it('spaces samples a step apart along the line', () => {
    const line = northSouthLine(52, 52.1, 10) // about 11.1 km in ten segments
    const samples = resampleByDistance(line, 1, 5)

    expect(samples.length).toBe(12) // the start, then one per kilometre
    for (let i = 1; i < samples.length; i++) {
      expect(distanceKm(samples[i - 1]!, samples[i]!)).toBeCloseTo(1, 2)
    }
  })

  it('keeps sampling evenly across vertices rather than restarting at each one', () => {
    // Vertices every 0.4 km with a 1 km step: without carrying the remainder across vertices this
    // would emit nothing at all, since no single segment is a full step long.
    const line = northSouthLine(52, 52.1, 28)
    const samples = resampleByDistance(line, 1, 5)
    expect(samples.length).toBeGreaterThan(10)
    expect(distanceKm(samples[0]!, samples[1]!)).toBeCloseTo(1, 1)
  })

  it('returns the first coordinate of a line shorter than one step', () => {
    const line: LatLng[] = [
      [52, 5],
      [52.001, 5],
    ]
    expect(resampleByDistance(line, 1, 5)).toEqual([[52, 5]])
  })

  it('steps over a gap instead of sampling across it', () => {
    // 2 km driven, a 55 km gap, then 2 km driven again.
    const line: LatLng[] = [
      [52, 5],
      [52.018, 5],
      [52.518, 5],
      [52.536, 5],
    ]
    const samples = resampleByDistance(line, 1, 5)

    // Nothing lands inside the gap: no sample sits between the two stretches.
    const insideGap = samples.filter((s) => s[0] > 52.02 && s[0] < 52.517)
    expect(insideGap).toEqual([])
    expect(samples.length).toBeGreaterThanOrEqual(5)
  })

  it('handles degenerate input without hanging', () => {
    expect(resampleByDistance([], 1, 5)).toEqual([])
    expect(resampleByDistance([[52, 5]], 1, 5)).toEqual([[52, 5]])
    expect(
      resampleByDistance(
        [
          [52, 5],
          [52, 5],
        ],
        1,
        5,
      ),
    ).toEqual([[52, 5]])
    // A step of zero would otherwise loop forever.
    expect(
      resampleByDistance(
        [
          [52, 5],
          [52.1, 5],
        ],
        0,
        5,
      ),
    ).toEqual([[52, 5]])
  })
})
