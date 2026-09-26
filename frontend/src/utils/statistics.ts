import type { TripSummary } from '@/services/vehicleApi'

/**
 * The trip figures the statistics page shows, read from trip summaries rather than from every
 * fix of the period. Each returns null when there are no trips to say anything about.
 */

export interface MapSpot {
  lat: number
  lng: number
}

/** Kilometres driven over all trips, unrounded: the unit formatter rounds once, in its own unit. */
export function totalDistanceKm(trips: readonly TripSummary[]): number | null {
  if (trips.length === 0) return null
  return trips.reduce((sum, trip) => sum + trip.distanceKm, 0)
}

export function averageTripKm(trips: readonly TripSummary[]): number | null {
  const total = totalDistanceKm(trips)
  return total === null ? null : total / trips.length
}

/** The local hour most trips started in, as "HH:00". On a tie, the hour an earlier trip started in wins. */
export function peakDriveHour(trips: readonly TripSummary[]): string | null {
  if (trips.length === 0) return null
  const counts = new Map<number, number>()
  for (const trip of trips) {
    const hour = new Date(trip.startedAt).getHours()
    counts.set(hour, (counts.get(hour) ?? 0) + 1)
  }
  let bestHour = 0
  let bestCount = 0
  for (const [hour, count] of counts) {
    if (count > bestCount) {
      bestHour = hour
      bestCount = count
    }
  }
  return `${String(bestHour).padStart(2, '0')}:00`
}

/**
 * Where the trips ended, one entry per spot: ends that agree to three decimals (about 100 m) are
 * the same place visited again.
 */
export function parkingSpots(trips: readonly TripSummary[]): MapSpot[] {
  const spots = new Map<string, MapSpot>()
  for (const trip of trips) {
    if (trip.endLatitude === null || trip.endLongitude === null) continue
    const key = `${trip.endLatitude.toFixed(3)},${trip.endLongitude.toFixed(3)}`
    if (!spots.has(key)) spots.set(key, { lat: trip.endLatitude, lng: trip.endLongitude })
  }
  return [...spots.values()]
}

/**
 * The average speed while moving over every trip, each weighed by how many readings its own
 * average covers, which equals the mean of all those readings together.
 */
export function averageMovingSpeedKmh(trips: readonly TripSummary[]): number | null {
  let weighted = 0
  let samples = 0
  for (const trip of trips) {
    if (trip.avgMovingSpeedKmh === null || trip.movingSpeedSamples === 0) continue
    weighted += trip.avgMovingSpeedKmh * trip.movingSpeedSamples
    samples += trip.movingSpeedSamples
  }
  return samples > 0 ? weighted / samples : null
}

/** Whether any trip reported a speed at all, which is what makes a speed figure worth showing. */
export function hasSpeedReadings(trips: readonly TripSummary[]): boolean {
  return trips.some((trip) => trip.maxSpeedKmh !== null)
}
