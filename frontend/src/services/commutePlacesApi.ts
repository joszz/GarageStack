import { request, requestJson } from '@/services/apiCore'

export type CommutePlaceKind = 'home' | 'work'

export const COMMUTE_PLACE_KINDS: readonly CommutePlaceKind[] = ['home', 'work']

/** As the API's CommutePlaceLimits.LabelMaxLength. */
export const COMMUTE_PLACE_LABEL_MAX_LENGTH = 60

/** The radius a place may have, in metres, as the API checks it. */
export const COMMUTE_RADIUS = { min: 50, max: 1000, step: 50, default: 250 } as const

/** Home or a work place; trips between them are labelled as commutes. */
export interface CommutePlace {
  id: number
  kind: CommutePlaceKind
  label: string | null
  latitude: number
  longitude: number
  radiusMeters: number
}

export type CommutePlaceInput = Omit<CommutePlace, 'id'>

/** The place as saved (null once removed), and how many trips it marked and unmarked as commutes. */
export interface CommutePlaceChange {
  place: CommutePlace | null
  marked: number
  cleared: number
}

const base = (vin: string) => `/api/vehicles/${vin}/commute-places`

export const commutePlacesApi = {
  list: (vin: string) => request<CommutePlace[]>(base(vin)),
  create: (vin: string, input: CommutePlaceInput) =>
    requestJson<CommutePlaceChange>(base(vin), 'POST', input),
  update: (vin: string, id: number, input: CommutePlaceInput) =>
    requestJson<CommutePlaceChange>(`${base(vin)}/${id}`, 'PUT', input),
  remove: (vin: string, id: number) =>
    requestJson<CommutePlaceChange>(`${base(vin)}/${id}`, 'DELETE'),
}
