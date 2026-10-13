import { describe, it, expect } from 'vitest'
import type { DailyStat } from '@/services/vehicleApi'
import { tripSummary } from '@/services/__tests__/tripFixtures'
import {
  averageMovingSpeedKmh,
  averageTripKm,
  batteryVoltageChange,
  bucketAverages,
  bucketDays,
  bucketTickFormat,
  bucketTotals,
  hasDailyReadings,
  hasSpeedReadings,
  monthlyTotals,
  parkingSpots,
  peakDriveHour,
  roundedRange,
  totalDistanceKm,
} from '@/utils/statistics'

describe('trip statistics', () => {
  it('has nothing to say without trips', () => {
    expect(totalDistanceKm([])).toBeNull()
    expect(averageTripKm([])).toBeNull()
    expect(peakDriveHour([])).toBeNull()
    expect(averageMovingSpeedKmh([])).toBeNull()
    expect(parkingSpots([])).toEqual([])
    expect(hasSpeedReadings([])).toBe(false)
  })

  it('adds up and averages the distance', () => {
    const trips = [tripSummary({ distanceKm: 12.5 }), tripSummary({ distanceKm: 7.5 })]

    expect(totalDistanceKm(trips)).toBe(20)
    expect(averageTripKm(trips)).toBe(10)
  })

  it('finds the hour most trips start in, the one seen first winning a tie', () => {
    const at = (hour: number) => new Date(2026, 0, 1, hour, 30).toISOString()
    const trips = [at(17), at(8), at(8), at(17), at(9)].map((startedAt) =>
      tripSummary({ startedAt }),
    )

    expect(peakDriveHour(trips)).toBe(17)
    expect(peakDriveHour([tripSummary({ startedAt: at(7) })])).toBe(7)
  })

  it('counts a spot visited again once and skips trips without an end', () => {
    const spots = parkingSpots([
      tripSummary({ endLatitude: 52.10001, endLongitude: 5.10001 }),
      tripSummary({ endLatitude: 52.10002, endLongitude: 5.10004 }),
      tripSummary({ endLatitude: 52.2, endLongitude: 5.3 }),
      tripSummary({ endLatitude: null, endLongitude: null }),
    ])

    expect(spots).toEqual([
      { lat: 52.10001, lng: 5.10001 },
      { lat: 52.2, lng: 5.3 },
    ])
  })

  it('weighs each trip by the readings behind its average speed', () => {
    const trips = [
      tripSummary({ avgMovingSpeedKmh: 30, movingSpeedSamples: 30 }),
      tripSummary({ avgMovingSpeedKmh: 110, movingSpeedSamples: 10 }),
      tripSummary({ avgMovingSpeedKmh: null, movingSpeedSamples: 0 }),
    ]

    expect(averageMovingSpeedKmh(trips)).toBe(50)
  })

  it('knows whether any trip reported a speed', () => {
    expect(hasSpeedReadings([tripSummary({ maxSpeedKmh: null })])).toBe(false)
    expect(
      hasSpeedReadings([tripSummary({ maxSpeedKmh: null }), tripSummary({ maxSpeedKmh: 0 })]),
    ).toBe(true)
  })
})

export function day(date: string, overrides: Partial<DailyStat> = {}): DailyStat {
  return {
    date,
    evSocPercent: null,
    fuelLevelPercent: null,
    tyrePressureFrontLeft: null,
    tyrePressureFrontRight: null,
    tyrePressureRearLeft: null,
    tyrePressureRearRight: null,
    batteryVoltage: null,
    counterTotal: null,
    ...overrides,
  }
}

describe('days, weeks and months', () => {
  it('keeps every day its own point when charting per day', () => {
    const buckets = bucketDays([day('2026-09-25'), day('2026-09-26')], 'day')

    expect(buckets.map((b) => b.key)).toEqual(['2026-09-25', '2026-09-26'])
    expect(buckets[0]!.start).toEqual(new Date(2026, 8, 25))
  })

  // 21 September 2026 is a Monday.
  it('groups days into weeks from Monday', () => {
    const days = ['2026-09-20', '2026-09-21', '2026-09-27', '2026-09-28'].map((d) => day(d))

    expect(bucketDays(days, 'week').map((b) => [b.key, b.days.length])).toEqual([
      ['2026-09-14', 1],
      ['2026-09-21', 2],
      ['2026-09-28', 1],
    ])
  })

  it('groups days into calendar months', () => {
    const days = ['2026-08-31', '2026-09-01', '2026-09-30'].map((d) => day(d))

    expect(bucketDays(days, 'month').map((b) => [b.key, b.days.length])).toEqual([
      ['2026-08-01', 1],
      ['2026-09-01', 2],
    ])
  })

  it('averages levels and adds up energy over a point, leaving an empty point empty', () => {
    const buckets = bucketDays(
      [
        day('2026-09-21', { evSocPercent: 60, counterTotal: 2 }),
        day('2026-09-22', { evSocPercent: 80, counterTotal: 3.5 }),
        day('2026-09-28'),
      ],
      'week',
    )

    expect(bucketAverages(buckets, (d) => d.evSocPercent)).toEqual([70, null])
    expect(bucketTotals(buckets, (d) => d.counterTotal)).toEqual([5.5, null])
  })

  it('names months on the axis by name, and adds the year across a new year', () => {
    const sameYear = bucketDays([day('2026-01-05'), day('2026-03-05')], 'month')
    const twoYears = bucketDays([day('2025-12-05'), day('2026-01-05')], 'month')

    expect(bucketTickFormat(sameYear, 'month')).toEqual({ month: 'short' })
    expect(bucketTickFormat(twoYears, 'month')).toEqual({ month: 'short', year: 'numeric' })
    expect(bucketTickFormat(sameYear, 'day')).toEqual({ day: 'numeric', month: 'short' })
  })

  it('knows whether the car reported anything in the period', () => {
    expect(hasDailyReadings([day('2026-09-25'), day('2026-09-26')])).toBe(false)
    expect(hasDailyReadings([day('2026-09-25', { batteryVoltage: 12.5 })])).toBe(true)
  })

  it('tracks the 12 V battery from its first day with a reading to its last', () => {
    const days = [
      day('2026-09-24', { batteryVoltage: 12.6 }),
      day('2026-09-25'),
      day('2026-09-26', { batteryVoltage: 12.45 }),
    ]

    expect(batteryVoltageChange(days)).toBe(-0.15)
    expect(batteryVoltageChange([day('2026-09-24')])).toBeNull()
  })

  it('widens a chart range to round numbers in the unit shown', () => {
    expect(roundedRange(1.5, 3.5)).toEqual({ min: 1.5, max: 3.5 })
    expect(roundedRange(21.76, 50.76)).toEqual({ min: 20, max: 55 })
  })
})

describe('monthlyTotals', () => {
  it('lists every month of the period with its trips and energy', () => {
    const trips = [
      tripSummary({ startedAt: new Date(2026, 7, 30, 9).toISOString(), distanceKm: 12 }),
      tripSummary({ startedAt: new Date(2026, 8, 2, 9).toISOString(), distanceKm: 30 }),
      tripSummary({ startedAt: new Date(2026, 8, 3, 9).toISOString(), distanceKm: 8 }),
    ]
    const days = [
      day('2026-08-30', { counterTotal: 1.5 }),
      day('2026-09-02', { counterTotal: 4 }),
      day('2026-09-03', { counterTotal: 1 }),
    ]

    const months = monthlyTotals(trips, days, new Date(2026, 6, 20), new Date(2026, 8, 3))

    expect(months.map((m) => [m.key, m.trips, m.distanceKm, m.counterTotal])).toEqual([
      ['2026-07', 0, 0, null],
      ['2026-08', 1, 12, 1.5],
      ['2026-09', 2, 38, 5],
    ])
  })
})
