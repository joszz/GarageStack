import type { Trip, TripPoint, TripSummary } from '@/services/vehicleApi'

/**
 * Trip test data shared by the specs of everything that reads a trip. Not a spec itself, so
 * importing it does not run anything twice. Specs override only the fields their assertions read.
 */

/** A 42 km trip from Zwolle to Deventer on 24 September 2026, 14:32 to 15:06 UTC, no speeds. */
export function tripSummary(overrides: Partial<TripSummary> = {}): TripSummary {
  return {
    index: 0,
    id: 1,
    startedAt: '2026-09-24T14:32:00.000Z',
    endedAt: '2026-09-24T15:06:00.000Z',
    distanceKm: 42,
    pointCount: 2,
    endLatitude: 52.2554,
    endLongitude: 6.1639,
    maxSpeedKmh: null,
    avgMovingSpeedKmh: null,
    movingSpeedSamples: 0,
    ...overrides,
  }
}

/** The same trip with its fixes. The point count follows the fixes unless it is overridden. */
export function trip(overrides: Partial<Trip> = {}): Trip {
  const points: TripPoint[] = overrides.points ?? [
    { recordedAt: '2026-09-24T14:32:00.000Z', latitude: 52.5123, longitude: 6.0921, speed: null },
    { recordedAt: '2026-09-24T15:06:00.000Z', latitude: 52.2554, longitude: 6.1639, speed: null },
  ]
  return { ...tripSummary({ pointCount: points.length }), points, ...overrides }
}
