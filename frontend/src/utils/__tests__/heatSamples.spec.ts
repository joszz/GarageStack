import { describe, it, expect } from 'vitest'
import { distanceWeightedSamples } from '@/utils/heatSamples'
import { type LatLng } from '@/utils/geo'

/** A straight north-south line of `count` fixes, `spacingKm` apart, at longitude `lng`. */
function line(count: number, spacingKm: number, lng: number, fromLat = 52): LatLng[] {
  const degreesPerKm = 1 / 111.19
  return Array.from(
    { length: count },
    (_, i) => [fromLat + i * spacingKm * degreesPerKm, lng] as LatLng,
  )
}

const near = (samples: LatLng[], lng: number) =>
  samples.filter((s) => Math.abs(s[1] - lng) < 0.5).length

describe('distanceWeightedSamples', () => {
  it('weights a jam by the road it covers rather than by how many fixes it left', () => {
    // A queue: 200 fixes within 200 m, as telemetry on a clock produces while standing still.
    const jam = line(200, 0.001, 4)
    // Moving: 20 fixes over 20 km, the same drive time at speed.
    const driving = line(20, 1, 6)

    const samples = distanceWeightedSamples([jam, driving], 101)

    // By fix count the jam would be ten times the hotter of the two; by distance it is a rounding
    // error next to a 20 km stretch.
    expect(near(samples, 4)).toBeLessThanOrEqual(2)
    expect(near(samples, 6)).toBeGreaterThan(50)
  })

  it('makes a road driven twice about twice as hot as one driven once', () => {
    const twice = line(101, 0.1, 4)
    const once = line(101, 0.1, 6)

    const samples = distanceWeightedSamples([twice, twice, once], 300)

    const ratio = near(samples, 4) / near(samples, 6)
    expect(ratio).toBeGreaterThan(1.7)
    expect(ratio).toBeLessThan(2.3)
  })

  it('holds to the budget however far the trips run', () => {
    // 10000 km of driving against a 500 point budget: the spacing grows to match rather than the
    // canvas filling up. The one extra point is the last sample, which downsample always keeps.
    const lines = Array.from({ length: 20 }, (_, i) => line(500, 1, i * 2))
    expect(distanceWeightedSamples(lines, 500).length).toBeLessThanOrEqual(501)
  })

  it('keeps short trips on the map', () => {
    const shortHop: LatLng[] = [
      [52, 5],
      [52.0005, 5],
    ]
    const longDrive = line(100, 1, 7)

    const samples = distanceWeightedSamples([shortHop, longDrive], 100)
    expect(near(samples, 5)).toBe(1)
  })

  it('has nothing to draw without lines or budget', () => {
    expect(distanceWeightedSamples([], 100)).toEqual([])
    expect(distanceWeightedSamples([line(10, 1, 5)], 0)).toEqual([])
  })
})
