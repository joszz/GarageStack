import { buildQuery, request, requestJson } from '@/services/apiCore'
import { MAX_GEOCODE_POINTS_PER_REQUEST, type Place } from '@/services/mapApi'

/** What a trip was for, in the order the trip log offers them. */
export const TRIP_PURPOSES = ['business', 'commute', 'private'] as const
export type TripPurpose = (typeof TRIP_PURPOSES)[number]

/** Mirrors Trip.NotesMaxLength: the server rejects longer notes. */
export const TRIP_NOTES_MAX_LENGTH = 500

/** Mirrors TripPlaceService.MaxTripsPerRequest: two ends per trip, within one geocode batch. */
export const MAX_TRIPS_PER_PLACES_REQUEST = MAX_GEOCODE_POINTS_PER_REQUEST / 2

/** Mirrors TripEndpoints.MaxTripsPerPurposeChange. */
export const MAX_TRIPS_PER_PURPOSE_CHANGE = 1000

/** One saved trip as the trip log shows it: no fixes, just its ends and what was recorded about it. */
export interface TripLogEntry {
  id: number
  startedAt: string
  endedAt: string
  /** Along the GPS fixes, which cut corners; the odometer readings are the better figure. */
  distanceKm: number
  startLatitude: number
  startLongitude: number
  endLatitude: number
  endLongitude: number
  odometerStartKm: number | null
  odometerEndKm: number | null
  /** Null until looked up. A place with every field null means nothing is mapped there. */
  startPlace: Place | null
  endPlace: Place | null
  purpose: TripPurpose | null
  notes: string | null
}

export interface TripPlaces {
  id: number
  startPlace: Place | null
  endPlace: Place | null
}

export interface TripPlacesResult {
  trips: TripPlaces[]
  /** False when the deployment has geocoding switched off, so the caller can stop asking. */
  available: boolean
  /** True while ends remain unresolved; asking again later fills them in. */
  hasMore: boolean
}

export const tripLogApi = {
  log: (vin: string, from: string, to: string) =>
    request<TripLogEntry[]>(`/api/vehicles/${vin}/trips/log${buildQuery({ from, to })}`),
  update: (vin: string, id: number, purpose: TripPurpose | null, notes: string | null) =>
    requestJson<TripLogEntry>(`/api/vehicles/${vin}/trips/${id}`, 'PUT', { purpose, notes }),
  setPurpose: (vin: string, ids: number[], purpose: TripPurpose | null) =>
    requestJson<{ changed: number }>(`/api/vehicles/${vin}/trips/purpose`, 'POST', {
      ids,
      purpose,
    }),
  // Batched, and the addresses found are kept on the trips, so a trip is looked up only once.
  resolvePlaces: (vin: string, ids: number[], language: string) =>
    requestJson<TripPlacesResult>(`/api/vehicles/${vin}/trips/places`, 'POST', {
      ids,
      language,
    }),
}
