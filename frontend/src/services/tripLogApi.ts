import { buildQuery, request, requestJson } from '@/services/apiCore'
import { MAX_GEOCODE_POINTS_PER_REQUEST, type Place } from '@/services/mapApi'
import type { TripPoint } from '@/services/vehicleApi'

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
  /** Marked a commute from home and work, rather than chosen by someone. */
  purposeIsAutomatic: boolean
  /** Left out of the map, the statistics, the dashboard and this log's totals and export. */
  isHidden: boolean
  /** Its route was edited by hand. */
  isEdited: boolean
}

/**
 * Where the car stood before or after a trip: a parked fix, or the neighbouring trip's end.
 * `suggestedAt` is the time to give a point placed there: when the car left it, or got there.
 */
export interface TripStop {
  latitude: number
  longitude: number
  recordedAt: string
  suggestedAt: string
}

/** What the route editor needs for a saved trip (`GET .../trips/{id}/editor`). */
export interface TripEditor {
  id: number
  /** The route as it is now. */
  points: TripPoint[]
  /** The route as recorded, once the trip was edited; null while it is as recorded. */
  originalPoints: TripPoint[] | null
  editedAt: string | null
  previousStop: TripStop | null
  nextStop: TripStop | null
  /** The trip probably left from `previousStop`: one tap fixes a trip saved before anchoring. */
  suggestion: boolean
  /** No point may be earlier than the trip before it ended. */
  notBefore: string | null
  /** Nor later than the next trip started, or the trips not saved yet. */
  notAfter: string | null
  /** The most points a save may send. */
  maxPoints: number
}

/** A point as the editor sends it; the server decides its speed and source. */
export interface TripPointInput {
  recordedAt: string
  latitude: number
  longitude: number
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
  setHidden: (vin: string, id: number, hidden: boolean) =>
    requestJson<TripLogEntry>(`/api/vehicles/${vin}/trips/${id}/hidden`, 'PUT', { hidden }),
  // Joins the trip with the saved trip after it; the joined trip keeps this one's id.
  mergeWithNext: (vin: string, id: number) =>
    requestJson<TripLogEntry>(`/api/vehicles/${vin}/trips/${id}/merge-next`, 'POST'),
  setPurpose: (vin: string, ids: number[], purpose: TripPurpose | null) =>
    requestJson<{ changed: number }>(`/api/vehicles/${vin}/trips/purpose`, 'POST', {
      ids,
      purpose,
    }),
  editor: (vin: string, id: number) =>
    request<TripEditor>(`/api/vehicles/${vin}/trips/${id}/editor`),
  // The whole route at once; the answer is the editor as the trip now is.
  setPoints: (vin: string, id: number, points: TripPointInput[]) =>
    requestJson<TripEditor>(`/api/vehicles/${vin}/trips/${id}/points`, 'PUT', { points }),
  resetPoints: (vin: string, id: number) =>
    requestJson<TripEditor>(`/api/vehicles/${vin}/trips/${id}/points/reset`, 'POST'),
  // Batched, and the addresses found are kept on the trips, so a trip is looked up only once.
  resolvePlaces: (vin: string, ids: number[], language: string) =>
    requestJson<TripPlacesResult>(`/api/vehicles/${vin}/trips/places`, 'POST', {
      ids,
      language,
    }),
}
