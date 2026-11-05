import { describe, it, expect } from 'vitest'
import { deventer, logEntry, place } from '@/services/__tests__/tripLogFixtures'
import {
  distanceShare,
  endLabel,
  loggedDistanceKm,
  missingPlaces,
  odometerDistanceKm,
  periodKey,
  periodRange,
  periodSummary,
  purposeTotals,
  tripDurationMs,
} from '../tripLog'

describe('periodRange', () => {
  it('covers a month from its first local midnight up to the next month', () => {
    const { from, to } = periodRange({ year: 2026, month: 8 })
    expect(from).toEqual(new Date(2026, 8, 1))
    expect(to).toEqual(new Date(2026, 9, 1))
  })

  it('rolls December over into the next year', () => {
    expect(periodRange({ year: 2026, month: 11 }).to).toEqual(new Date(2027, 0, 1))
  })

  it('covers a whole year when no month is chosen', () => {
    const { from, to } = periodRange({ year: 2026, month: null })
    expect(from).toEqual(new Date(2026, 0, 1))
    expect(to).toEqual(new Date(2027, 0, 1))
  })
})

describe('periodKey', () => {
  it('names a month by year and zero-padded month, and a year by itself', () => {
    expect(periodKey({ year: 2026, month: 0 })).toBe('2026-01')
    expect(periodKey({ year: 2026, month: 11 })).toBe('2026-12')
    expect(periodKey({ year: 2026, month: null })).toBe('2026')
  })
})

describe('distances', () => {
  it('counts a trip for the distance on the odometer when both readings are there', () => {
    const entry = logEntry()
    expect(odometerDistanceKm(entry)).toBe(42.5)
    expect(loggedDistanceKm(entry)).toBe(42.5)
  })

  it('falls back to the GPS distance without both readings', () => {
    expect(odometerDistanceKm(logEntry({ odometerStartKm: null }))).toBeNull()
    expect(loggedDistanceKm(logEntry({ odometerEndKm: null }))).toBe(40)
  })

  it('does not trust readings that run backwards', () => {
    const entry = logEntry({ odometerStartKm: 24052.5, odometerEndKm: 24010 })
    expect(odometerDistanceKm(entry)).toBeNull()
    expect(loggedDistanceKm(entry)).toBe(40)
  })
})

describe('purposeTotals', () => {
  it('adds up trips and distance per purpose, with unclassified trips on their own', () => {
    const totals = purposeTotals([
      logEntry({ id: 1, purpose: 'business' }),
      logEntry({ id: 2, purpose: 'business', odometerEndKm: null }),
      logEntry({ id: 3, purpose: 'private' }),
      logEntry({ id: 4 }),
    ])

    expect(totals.business).toEqual({ trips: 2, km: 82.5 })
    expect(totals.commute).toEqual({ trips: 0, km: 0 })
    expect(totals.private).toEqual({ trips: 1, km: 42.5 })
    expect(totals.unclassified).toEqual({ trips: 1, km: 42.5 })
  })
})

describe('endLabel', () => {
  it('reads as the address when known, and as coordinates otherwise', () => {
    expect(endLabel(deventer, 52.2554, 6.1639)).toBe('Brink 2, 7411 BT Deventer')
    expect(endLabel(null, 52.2554, 6.1639)).toBe('52.25540, 6.16390')
  })

  it('reads as coordinates where nothing is mapped', () => {
    expect(endLabel(place(), 52.2554, 6.1639)).toBe('52.25540, 6.16390')
  })
})

describe('missingPlaces', () => {
  it('is true until both ends have been looked up, blank answers included', () => {
    expect(missingPlaces(logEntry())).toBe(true)
    expect(missingPlaces(logEntry({ startPlace: deventer }))).toBe(true)
    expect(missingPlaces(logEntry({ startPlace: deventer, endPlace: deventer }))).toBe(false)
  })
})

describe('tripDurationMs', () => {
  it('is the time from leaving to arriving, and never negative', () => {
    expect(tripDurationMs(logEntry())).toBe(34 * 60_000)
    expect(
      tripDurationMs(
        logEntry({ startedAt: '2026-09-24T13:00:00Z', endedAt: '2026-09-24T12:00:00Z' }),
      ),
    ).toBe(0)
  })
})

describe('periodSummary', () => {
  it('adds up distance and driving time, and reads the averages and longest trip off them', () => {
    const summary = periodSummary([
      logEntry(),
      logEntry({ id: 2, odometerStartKm: null, distanceKm: 17.5 }),
    ])

    expect(summary.trips).toBe(2)
    expect(summary.km).toBeCloseTo(60)
    expect(summary.drivingMs).toBe(68 * 60_000)
    expect(summary.averageTripKm).toBeCloseTo(30)
    expect(summary.longestTripKm).toBeCloseTo(42.5)
    expect(summary.averageSpeedKmh).toBeCloseTo(60 / (68 / 60))
  })

  it('has no averages without trips', () => {
    expect(periodSummary([])).toEqual({
      trips: 0,
      km: 0,
      drivingMs: 0,
      averageTripKm: null,
      longestTripKm: null,
      averageSpeedKmh: null,
    })
  })
})

describe('distanceShare', () => {
  it('is the part of the distance, and 0 when nothing was driven', () => {
    expect(distanceShare({ trips: 1, km: 25 }, 100)).toBe(0.25)
    expect(distanceShare({ trips: 0, km: 0 }, 0)).toBe(0)
  })
})
