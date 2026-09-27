import { describe, it, expect } from 'vitest'
import type { TripMatch } from '@/composables/useTripMatch'
import type { Trip, TripPoint } from '@/services/vehicleApi'
import { speedSegments, speedToColor, tripColor, tripColorClass } from '@/utils/speedColors'

function point(latitude: number, speed: number | null): TripPoint {
  return { recordedAt: '2026-09-26T10:00:00Z', latitude, longitude: 5, speed }
}

function trip(points: TripPoint[]): Trip {
  return {
    index: 0,
    id: 1,
    startedAt: '2026-09-26T10:00:00Z',
    endedAt: '2026-09-26T10:10:00Z',
    distanceKm: 1,
    pointCount: points.length,
    endLatitude: null,
    endLongitude: null,
    maxSpeedKmh: null,
    avgMovingSpeedKmh: null,
    movingSpeedSamples: 0,
    points,
  }
}

describe('tripColor', () => {
  it('takes the colours in turn, and the list dots follow the same turn', () => {
    expect(tripColor(0)).toBe('#3b82f6')
    expect(tripColor(6)).toBe(tripColor(0))
    expect(tripColorClass(7)).toBe('trip-list__dot--1')
  })
})

describe('speedToColor', () => {
  it('uses the fallback for a fix without a speed', () => {
    expect(speedToColor(null, '#abcdef')).toBe('#abcdef')
  })

  it('is green standing still and red at motorway speeds and above', () => {
    expect(speedToColor(0, '')).toBe('rgb(16,185,129)')
    expect(speedToColor(150, '')).toBe('rgb(239,68,68)')
    expect(speedToColor(200, '')).toBe('rgb(239,68,68)')
  })

  it('blends between two stops', () => {
    expect(speedToColor(25, '')).toBe('rgb(74,195,76)')
  })
})

describe('speedSegments', () => {
  it('joins consecutive fixes when there is no snapped line', () => {
    const segments = speedSegments(trip([point(52, 10), point(52.1, 20), point(52.2, 30)]), null)

    expect(segments).toEqual([
      {
        coordinates: [
          [52, 5],
          [52.1, 5],
        ],
        speed: 10,
      },
      {
        coordinates: [
          [52.1, 5],
          [52.2, 5],
        ],
        speed: 20,
      },
    ])
  })

  it('follows the snapped line between the vertices each fix was placed on', () => {
    const fixes = [point(52, 10), point(52.1, 20), point(52.2, 30)]
    const match: TripMatch = {
      coordinates: [
        [52, 5],
        [52.05, 5.01],
        [52.1, 5],
        [52.15, 5.01],
        [52.2, 5],
        [52.25, 5.01],
      ],
      points: fixes,
      pointIndexes: [0, 2, 4],
      matchedKm: 1,
      speedLimits: [],
    }

    const segments = speedSegments(trip(fixes), match)

    expect(segments.map((s) => s.coordinates.length)).toEqual([3, 3, 2])
    expect(segments.map((s) => s.speed)).toEqual([10, 20, 30])
  })

  it('carries past fixes the matcher put on the same vertex, leaving no gap', () => {
    const fixes = [point(52, 10), point(52.1, 20), point(52.2, 30)]
    const match: TripMatch = {
      coordinates: [
        [52, 5],
        [52.1, 5],
        [52.2, 5],
      ],
      points: fixes,
      pointIndexes: [0, 0, 2],
      matchedKm: 1,
      speedLimits: [],
    }

    const segments = speedSegments(trip(fixes), match)

    expect(segments).toHaveLength(1)
    expect(segments[0]!.coordinates).toHaveLength(3)
  })
})
