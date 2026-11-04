import { describe, it, expect } from 'vitest'
import type { TripPoint } from '@/services/vehicleApi'
import type { TripStop } from '@/services/tripLogApi'
import {
  isCarFix,
  routeKm,
  sameRoute,
  stopIsElsewhere,
  toInput,
  withEnd,
  withNextStop,
  withPointAdded,
  withPointRemoved,
  withPreviousStop,
  withStart,
  type TripLimits,
} from '../tripEdit'

const T0 = Date.parse('2026-01-01T10:00:00.000Z')
const at = (minutes: number) => new Date(T0 + minutes * 60_000).toISOString()

// A drive north, a fix every five minutes: about 5.6 km a leg.
const fix = (minutes: number, latitude: number): TripPoint => ({
  recordedAt: at(minutes),
  latitude,
  longitude: 5,
  speed: 50,
})
const route: TripPoint[] = [fix(0, 52.0), fix(5, 52.05), fix(10, 52.1)]
const open: TripLimits = { notBefore: null, notAfter: null }

describe('tripEdit', () => {
  it('measures a route along its points', () => {
    expect(routeKm(route)).toBeCloseTo(11.12, 2)
    expect(routeKm(route.slice(0, 1))).toBe(0)
  })

  it('tells the same route from a different one', () => {
    expect(sameRoute(route, [...route])).toBe(true)
    expect(sameRoute(route, route.slice(1))).toBe(false)
    expect(sameRoute(route, [route[0]!, route[1]!, { ...route[2]!, latitude: 52.11 }])).toBe(false)
  })

  it('sends only where and when, keeping the recorded times as they came', () => {
    const precise = { ...route[0]!, recordedAt: '2026-01-01T10:00:00.123456Z' }

    expect(toInput([precise])).toEqual([
      { recordedAt: '2026-01-01T10:00:00.123456Z', latitude: 52.0, longitude: 5 },
    ])
  })

  describe('moving the start', () => {
    it('keeps the first fix of the car and puts a new point before it, at town speed', () => {
      const moved = withStart(route, 51.99, 5, open)

      expect(moved).toHaveLength(4)
      expect(moved.slice(1)).toEqual(route)
      expect(moved[0]).toMatchObject({ latitude: 51.99, speed: null, source: 'manual' })
      // 1.1 km at 25 km/h is a little over two and a half minutes.
      expect(Date.parse(moved[0]!.recordedAt)).toBeCloseTo(T0 - 160_000, -4)
    })

    it('moves a point someone placed instead of adding another', () => {
      const once = withStart(route, 51.99, 5, open)

      const twice = withStart(once, 51.98, 5, open)

      expect(twice).toHaveLength(4)
      expect(twice[0]!.latitude).toBe(51.98)
    })

    it('moves an anchored start, which the car did not report driving there', () => {
      const anchored = [{ ...route[0]!, source: 'anchored' as const }, ...route.slice(1)]

      expect(withStart(anchored, 51.99, 5, open)).toHaveLength(3)
      expect(isCarFix(anchored[0]!)).toBe(false)
    })

    it('never leaves before the trip before it ended', () => {
      const moved = withStart(route, 51.9, 5, { notBefore: at(-3), notAfter: null })

      expect(moved[0]!.recordedAt).toBe(at(-3))
    })

    it('leaves at the time a parking spot gives', () => {
      const stop: TripStop = {
        latitude: 51.99,
        longitude: 5,
        recordedAt: at(-600),
        suggestedAt: at(-4),
      }

      expect(withPreviousStop(route, stop, open)[0]).toMatchObject({
        latitude: 51.99,
        recordedAt: at(-4),
      })
    })
  })

  describe('moving the end', () => {
    it('keeps the last fix of the car and adds a point after it, never past the next trip', () => {
      const moved = withEnd(route, 52.2, 5, { notBefore: null, notAfter: at(12) })

      expect(moved.slice(0, 3)).toEqual(route)
      expect(moved[3]).toMatchObject({ latitude: 52.2, recordedAt: at(12), source: 'manual' })
    })

    it('reaches a parking spot at the time it gives', () => {
      const stop: TripStop = {
        latitude: 52.101,
        longitude: 5,
        recordedAt: at(11),
        suggestedAt: at(11),
      }

      expect(withNextStop(route, stop, open)[3]!.recordedAt).toBe(at(11))
    })
  })

  it('offers a parking spot only when it is somewhere else', () => {
    const near: TripStop = {
      latitude: 52.0003,
      longitude: 5,
      recordedAt: at(-1),
      suggestedAt: at(-1),
    }
    const far: TripStop = { ...near, latitude: 51.99 }

    expect(stopIsElsewhere(route[0], near)).toBe(false)
    expect(stopIsElsewhere(route[0], far)).toBe(true)
    expect(stopIsElsewhere(route[0], null)).toBe(false)
  })

  describe('points in between', () => {
    it('adds a point to the nearest segment, at a time as far along as it lies', () => {
      // A quarter of the way along the second leg, a little off to the side.
      const { points, index } = withPointAdded(route, 52.0625, 5.001)

      expect(index).toBe(2)
      expect(points).toHaveLength(4)
      expect(points[2]).toMatchObject({ latitude: 52.0625, source: 'manual', speed: null })
      expect(Date.parse(points[2]!.recordedAt)).toBeCloseTo(T0 + 6.25 * 60_000, -4)
    })

    it('never gives an added point the very time of a neighbour', () => {
      const { points } = withPointAdded(route, 51.9, 5)

      expect(Date.parse(points[1]!.recordedAt)).toBeGreaterThan(T0)
    })

    it('removes a point, but never below a start and an end', () => {
      expect(withPointRemoved(route, 1)).toEqual([route[0], route[2]])
      expect(withPointRemoved(route.slice(0, 2), 1)).toHaveLength(2)
      expect(withPointRemoved(route, 7)).toEqual(route)
    })
  })
})
