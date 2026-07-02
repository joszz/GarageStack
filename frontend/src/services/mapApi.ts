import { request, buildQuery } from '@/services/apiCore'

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
}
