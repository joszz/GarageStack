import { describe, it, expect } from 'vitest'
import type { TripPoint } from '@/services/vehicleApi'
import { addedStretches, phoneStretches } from '../tripSources'

const point = (latitude: number, source?: TripPoint['source']): TripPoint => ({
  recordedAt: '2026-01-01T10:00:00.000Z',
  latitude,
  longitude: 5,
  speed: null,
  ...(source ? { source } : {}),
})

describe('addedStretches', () => {
  it('keeps confirmed phone fixes apart from unobserved added stretches', () => {
    const points = [point(52), point(52.1, 'phone'), point(52.2), point(52.3, 'manual')]
    expect(phoneStretches(points)).toEqual([
      [
        [52, 5],
        [52.1, 5],
        [52.2, 5],
      ],
    ])
    expect(addedStretches(points)).toEqual([
      [
        [52.2, 5],
        [52.3, 5],
      ],
    ])
  })
  it('finds nothing on a trip the car reported all of', () => {
    expect(addedStretches([point(52.0), point(52.1), point(52.2)])).toEqual([])
  })

  it('runs straight to and from each placed point, joining neighbouring stretches', () => {
    const points = [
      point(52.0, 'anchored'),
      point(52.1),
      point(52.2),
      point(52.3, 'manual'),
      point(52.4),
    ]

    expect(addedStretches(points)).toEqual([
      [
        [52.0, 5],
        [52.1, 5],
      ],
      [
        [52.2, 5],
        [52.3, 5],
        [52.4, 5],
      ],
    ])
  })

  it('follows a snapped line between the vertices of the points', () => {
    const points = [point(52.0, 'anchored'), point(52.1), point(52.2)]
    const line = {
      coordinates: [
        [52.0, 5],
        [52.05, 5.01],
        [52.1, 5],
        [52.2, 5],
      ] as [number, number][],
      points,
      pointIndexes: [0, 2, 3],
    }

    expect(addedStretches(points, line)).toEqual([
      [
        [52.0, 5],
        [52.05, 5.01],
        [52.1, 5],
      ],
    ])
  })
})
