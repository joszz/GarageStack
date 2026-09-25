import type { Place } from '@/services/mapApi'
import type { TripLogEntry } from '@/services/tripLogApi'

/**
 * Trip log test data shared by the specs of everything that reads a trip log entry. Not a spec
 * itself, so importing it does not run anything twice.
 */

/** A trip from Zwolle to Deventer: 40 km along the fixes, 42.5 km on the odometer. */
export function logEntry(overrides: Partial<TripLogEntry> = {}): TripLogEntry {
  return {
    id: 1,
    startedAt: '2026-09-24T12:32:00.000Z',
    endedAt: '2026-09-24T13:06:00.000Z',
    distanceKm: 40,
    startLatitude: 52.5123,
    startLongitude: 6.0921,
    endLatitude: 52.2554,
    endLongitude: 6.1639,
    odometerStartKm: 24010,
    odometerEndKm: 24052.5,
    startPlace: null,
    endPlace: null,
    purpose: null,
    notes: null,
    ...overrides,
  }
}

export function place(overrides: Partial<Place> = {}): Place {
  return {
    road: null,
    houseNumber: null,
    city: null,
    postcode: null,
    countryCode: null,
    displayName: null,
    ...overrides,
  }
}

export const zwolle = place({
  road: 'Grote Markt',
  houseNumber: '1',
  city: 'Zwolle',
  postcode: '8011 PK',
  countryCode: 'nl',
})
export const deventer = place({
  road: 'Brink',
  houseNumber: '2',
  city: 'Deventer',
  postcode: '7411 BT',
  countryCode: 'nl',
})
