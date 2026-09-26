import { describe, it, expect } from 'vitest'
import type { TripSummary } from '@/services/vehicleApi'
import {
  averageMovingSpeedKmh,
  averageTripKm,
  hasSpeedReadings,
  parkingSpots,
  peakDriveHour,
  totalDistanceKm,
} from '@/utils/statistics'

function trip(overrides: Partial<TripSummary> = {}): TripSummary {
  return {
    index: 0,
    id: 1,
    startedAt: new Date(2026, 0, 1, 8, 15).toISOString(),
    endedAt: new Date(2026, 0, 1, 8, 45).toISOString(),
    distanceKm: 10,
    pointCount: 20,
    endLatitude: 52.1,
    endLongitude: 5.1,
    maxSpeedKmh: 80,
    avgMovingSpeedKmh: 50,
    movingSpeedSamples: 10,
    ...overrides,
  }
}

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
    const trips = [trip({ distanceKm: 12.5 }), trip({ distanceKm: 7.5 })]

    expect(totalDistanceKm(trips)).toBe(20)
    expect(averageTripKm(trips)).toBe(10)
  })

  it('finds the hour most trips start in, the one seen first winning a tie', () => {
    const at = (hour: number) => new Date(2026, 0, 1, hour, 30).toISOString()
    const trips = [at(17), at(8), at(8), at(17), at(9)].map((startedAt) => trip({ startedAt }))

    expect(peakDriveHour(trips)).toBe('17:00')
    expect(peakDriveHour([trip({ startedAt: at(7) })])).toBe('07:00')
  })

  it('counts a spot visited again once and skips trips without an end', () => {
    const spots = parkingSpots([
      trip({ endLatitude: 52.10001, endLongitude: 5.10001 }),
      trip({ endLatitude: 52.10002, endLongitude: 5.10004 }),
      trip({ endLatitude: 52.2, endLongitude: 5.3 }),
      trip({ endLatitude: null, endLongitude: null }),
    ])

    expect(spots).toEqual([
      { lat: 52.10001, lng: 5.10001 },
      { lat: 52.2, lng: 5.3 },
    ])
  })

  it('weighs each trip by the readings behind its average speed', () => {
    const trips = [
      trip({ avgMovingSpeedKmh: 30, movingSpeedSamples: 30 }),
      trip({ avgMovingSpeedKmh: 110, movingSpeedSamples: 10 }),
      trip({ avgMovingSpeedKmh: null, movingSpeedSamples: 0 }),
    ]

    expect(averageMovingSpeedKmh(trips)).toBe(50)
  })

  it('knows whether any trip reported a speed', () => {
    expect(hasSpeedReadings([trip({ maxSpeedKmh: null })])).toBe(false)
    expect(hasSpeedReadings([trip({ maxSpeedKmh: null }), trip({ maxSpeedKmh: 0 })])).toBe(true)
  })
})
