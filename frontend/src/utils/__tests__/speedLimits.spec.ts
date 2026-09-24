import { describe, it, expect } from 'vitest'
import {
  expandSpeedLimits,
  speedLimitSegments,
  speedLimitSummary,
  OVER_LIMIT_TOLERANCE_KPH,
  type SnappedLine,
} from '@/utils/speedLimits'

/**
 * A straight line north from the same point, one vertex every 100 metres, so a segment is 0.1 km
 * and distances in these tests are easy to read.
 */
function line(vertices: number): [number, number][] {
  return Array.from({ length: vertices }, (_, i) => [52.0 + i * 0.0009, 6.0] as [number, number])
}

/** A snapped trip: `vertices` of line, one speed reading per fix, one limit per segment. */
function snapped(
  vertices: number,
  fixes: { speed: number | null; at: number }[],
  limits: (number | null)[],
): SnappedLine {
  return {
    coordinates: line(vertices),
    points: fixes.map((f) => ({ speed: f.speed })),
    pointIndexes: fixes.map((f) => f.at),
    speedLimits: limits,
  }
}

describe('expandSpeedLimits', () => {
  it('unfolds run-length pairs into one limit per segment', () => {
    expect(expandSpeedLimits([2, 50, 3, 80], 5)).toEqual([50, 50, 80, 80, 80])
  })

  it('reads a limit of zero as not known', () => {
    expect(expandSpeedLimits([1, 0, 2, 30], 3)).toEqual([null, 30, 30])
  })

  it('leaves the rest unknown when the runs fall short', () => {
    expect(expandSpeedLimits([2, 50], 4)).toEqual([50, 50, null, null])
  })

  it('stops at the last segment when the runs overshoot', () => {
    expect(expandSpeedLimits([9, 50], 3)).toEqual([50, 50, 50])
  })

  it('has no limits without runs', () => {
    expect(expandSpeedLimits(null, 2)).toEqual([null, null])
    expect(expandSpeedLimits([], 2)).toEqual([null, null])
  })
})

describe('speedLimitSummary', () => {
  it('measures the trip in kilometres against the limits', () => {
    // Six segments of 100 m: the first three at 80 with the car doing 100, the rest at 80 with
    // the car doing 70.
    const match = snapped(
      7,
      [
        { speed: 100, at: 0 },
        { speed: 70, at: 3 },
      ],
      [80, 80, 80, 80, 80, 80],
    )

    const summary = speedLimitSummary(match)

    expect(summary.totalKm).toBeCloseTo(0.6, 2)
    expect(summary.knownKm).toBeCloseTo(0.6, 2)
    expect(summary.overKm).toBeCloseTo(0.3, 2)
    expect(summary.maxOverKph).toBe(20)
    expect(summary.maxOverLimitKph).toBe(80)
  })

  it('leaves stretches without a limit out of what is known', () => {
    const match = snapped(5, [{ speed: 90, at: 0 }], [50, null, null, 50])

    const summary = speedLimitSummary(match)

    expect(summary.totalKm).toBeCloseTo(0.4, 2)
    expect(summary.knownKm).toBeCloseTo(0.2, 2)
    expect(summary.overKm).toBeCloseTo(0.2, 2)
  })

  it('leaves stretches without a speed reading out of what is known', () => {
    const match = snapped(
      3,
      [
        { speed: null, at: 0 },
        { speed: 60, at: 1 },
      ],
      [50, 50],
    )

    const summary = speedLimitSummary(match)

    expect(summary.knownKm).toBeCloseTo(0.1, 2)
    expect(summary.overKm).toBeCloseTo(0.1, 2)
  })

  it('counts a reading within the tolerance as under the limit', () => {
    const atTolerance = snapped(2, [{ speed: 50 + OVER_LIMIT_TOLERANCE_KPH, at: 0 }], [50])
    const justOver = snapped(2, [{ speed: 50 + OVER_LIMIT_TOLERANCE_KPH + 1, at: 0 }], [50])

    expect(speedLimitSummary(atTolerance).overKm).toBe(0)
    expect(speedLimitSummary(justOver).overKm).toBeCloseTo(0.1, 2)
  })

  it('reports the worst overshoot with the limit it was against', () => {
    const match = snapped(
      4,
      [
        { speed: 60, at: 0 },
        { speed: 140, at: 1 },
        { speed: 95, at: 2 },
      ],
      [30, 100, 80],
    )

    const summary = speedLimitSummary(match)

    expect(summary.maxOverKph).toBe(40)
    expect(summary.maxOverLimitKph).toBe(100)
  })

  it('knows nothing about a trip whose roads carry no limits', () => {
    const match = snapped(3, [{ speed: 120, at: 0 }], [null, null])

    const summary = speedLimitSummary(match)

    expect(summary.totalKm).toBeCloseTo(0.2, 2)
    expect(summary.knownKm).toBe(0)
    expect(summary.overKm).toBe(0)
    expect(summary.maxOverLimitKph).toBeNull()
  })
})

describe('speedLimitSegments', () => {
  it('groups neighbouring segments in the same state into one stretch', () => {
    const match = snapped(
      5,
      [
        { speed: 90, at: 0 },
        { speed: 40, at: 2 },
      ],
      [50, 50, 50, 50],
    )

    const segments = speedLimitSegments(match)

    expect(segments.map((s) => s.state)).toEqual(['over', 'under'])
    expect(segments[0]!.coordinates).toHaveLength(3)
    expect(segments[1]!.coordinates).toHaveLength(3)
  })

  it('leaves no gap between stretches', () => {
    const match = snapped(
      4,
      [
        { speed: 90, at: 0 },
        { speed: 40, at: 1 },
        { speed: 90, at: 2 },
      ],
      [50, 50, 50],
    )

    const segments = speedLimitSegments(match)

    expect(segments).toHaveLength(3)
    for (let i = 1; i < segments.length; i++) {
      const previous = segments[i - 1]!.coordinates
      expect(segments[i]!.coordinates[0]).toEqual(previous[previous.length - 1])
    }
  })

  it('marks stretches without a limit as unknown', () => {
    const match = snapped(4, [{ speed: 90, at: 0 }], [50, null, 50])

    const segments = speedLimitSegments(match)

    expect(segments.map((s) => s.state)).toEqual(['over', 'unknown', 'over'])
  })

  it('has nothing to draw for a line of one vertex', () => {
    expect(speedLimitSegments(snapped(1, [{ speed: 50, at: 0 }], []))).toEqual([])
  })
})
