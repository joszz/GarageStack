import { request, requestJson, buildQuery } from '@/services/apiCore'

export interface Connector {
  type: string | null
  powerKw: number | null
  quantity: number | null
}

export interface ChargingStation {
  id: number
  title: string
  latitude: number
  longitude: number
  addressLine: string | null
  town: string | null
  operator: string | null
  isOperational: boolean | null
  numberOfPoints: number | null
  connectors: Connector[]
}

export interface PoiItem {
  externalId: string
  poiType: string
  latitude: number
  longitude: number
  name: string | null
  tags: Record<string, string> | null
}

export interface PoiResponse {
  items: PoiItem[]
  hasMore: boolean
}

/** Coarse ('city', for trip labels) or fine ('address', for the parked car) reverse geocoding. */
export type GeocodePrecision = 'city' | 'address'

export interface GeoPoint {
  lat: number
  lng: number
}

/** A resolved place. All-null fields mean the geocoder knows no place at that coordinate. */
export interface Place {
  road: string | null
  houseNumber: string | null
  city: string | null
  postcode: string | null
  countryCode: string | null
  displayName: string | null
}

export interface ReverseGeocodeResponse {
  /** One entry per requested point, in the same order; null where the lookup has not happened yet. */
  results: (Place | null)[]
  /** False when the deployment has geocoding switched off, so the caller can stop asking. */
  available: boolean
  /** True while points remain unresolved; asking again later fills them in. */
  hasMore: boolean
}

/** Mirrors GeocodeDefaults.MaxPointsPerRequest: the server rejects a larger batch. */
export const MAX_GEOCODE_POINTS_PER_REQUEST = 60

export const mapApi = {
  chargingStations: (
    lat: number,
    lng: number,
    distanceKm: number,
    minPowerKw = 0,
    maxPowerKw = 0,
  ) => {
    const query = buildQuery({ lat, lng, distanceKm, minPowerKw, maxPowerKw })
    return request<ChargingStation[]>(`/api/map/charging-stations${query}`)
  },
  poi: (type: string, lat: number, lng: number, radiusKm: number, vehicleType: string) => {
    const query = buildQuery({ type, lat, lng, radiusKm, vehicleType })
    return request<PoiResponse>(`/api/map/poi${query}`)
  },
  poiBrands: (type: string, vehicleType: string) => {
    const query = buildQuery({ type, vehicleType })
    return request<string[]>(`/api/map/poi/brands${query}`)
  },
  // Batched on purpose: a trip list asks about dozens of coordinates, and one request per trip
  // would eat the API's per-IP rate limit for no gain.
  reverseGeocode: (points: GeoPoint[], precision: GeocodePrecision, language: string) =>
    requestJson<ReverseGeocodeResponse>('/api/map/reverse', 'POST', {
      points,
      precision,
      language,
    }),
}
