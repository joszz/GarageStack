import { describe, it, expect } from 'vitest'
import type { TelemetryHistoryPoint } from '@/services/vehicleApi'
import { tripSummary } from '@/services/__tests__/tripFixtures'
import {
  averageMovingSpeedKmh,
  averageTripKm,
  batteryVoltageChange,
  dailyAverages,
  dailyCounterTotals,
  hasSpeedReadings,
  historyByDay,
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

function reading(at: Date, overrides: Partial<TelemetryHistoryPoint> = {}): TelemetryHistoryPoint {
  return {
    recordedAt: at.toISOString(),
    fuelLevelPercent: null,
    evSocPercent: null,
    powerUsageOfDay: null,
    batteryVoltage: null,
    climateOn: null,
    isCharging: null,
    tyrePressureFrontLeft: null,
    tyrePressureFrontRight: null,
    tyrePressureRearLeft: null,
    tyrePressureRearRight: null,
    mileageOfTheDay: null,
    mileageSinceLastCharge: null,
    hvSocKwh: null,
    hvTotalCapacityKwh: null,
    powerUsageSinceLastCharge: null,
    ...overrides,
  }
}

describe('daily history', () => {
  const now = new Date(2026, 8, 26, 15, 0)

  it('has a day for every date in the period, also the ones without readings', () => {
    const days = historyByDay([reading(new Date(2026, 8, 25, 9, 0))], 3, now)

    expect(days.map((d) => d.key)).toEqual(['2026-09-23', '2026-09-24', '2026-09-25', '2026-09-26'])
    expect(days.map((d) => d.points.length)).toEqual([0, 0, 1, 0])
  })

  it('averages a reading per day, leaving days without one empty', () => {
    const days = historyByDay(
      [
        reading(new Date(2026, 8, 25, 9, 0), { evSocPercent: 60 }),
        reading(new Date(2026, 8, 25, 18, 0), { evSocPercent: 80 }),
        reading(new Date(2026, 8, 26, 9, 0), { evSocPercent: null }),
      ],
      1,
      now,
    )

    expect(dailyAverages(days, (p) => p.evSocPercent)).toEqual([70, null])
  })

  it('tracks the 12 V battery from its first day to its last', () => {
    const days = historyByDay(
      [
        reading(new Date(2026, 8, 24, 9, 0), { batteryVoltage: 12.6 }),
        reading(new Date(2026, 8, 26, 9, 0), { batteryVoltage: 12.45 }),
      ],
      3,
      now,
    )

    expect(batteryVoltageChange(days)).toBe(-0.15)
    expect(batteryVoltageChange(historyByDay([], 3, now))).toBeNull()
  })

  it('reads a finished day off the counter peak and today with its reset in mind', () => {
    const days = historyByDay(
      [
        reading(new Date(2026, 8, 25, 9, 0), { powerUsageOfDay: 2 }),
        reading(new Date(2026, 8, 25, 18, 0), { powerUsageOfDay: 5.5 }),
        // Yesterday's figure still standing just after midnight, then the reset and a drive.
        reading(new Date(2026, 8, 26, 0, 5), { powerUsageOfDay: 5.5 }),
        reading(new Date(2026, 8, 26, 9, 0), { powerUsageOfDay: 0.1 }),
        reading(new Date(2026, 8, 26, 10, 0), { powerUsageOfDay: 1.5 }),
      ],
      1,
      now,
    )

    expect(dailyCounterTotals(days, now)).toEqual([5.5, 1.5])
  })

  it('widens a chart range to round numbers in the unit shown', () => {
    expect(roundedRange(1.5, 3.5)).toEqual({ min: 1.5, max: 3.5 })
    expect(roundedRange(21.76, 50.76)).toEqual({ min: 20, max: 55 })
  })
})
