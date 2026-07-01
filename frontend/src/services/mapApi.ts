import { request } from '@/services/apiCore'

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
    const params = new URLSearchParams({
      lat: String(lat),
      lng: String(lng),
      distanceKm: String(distanceKm),
      minPowerKw: String(minPowerKw),
      maxPowerKw: String(maxPowerKw),
    })
    return request<ChargingStation[]>(`/api/map/charging-stations?${params}`)
  },
  poi: (type: string, lat: number, lng: number, radiusKm: number, vehicleType: string) => {
    const params = new URLSearchParams({
      type,
      lat: String(lat),
      lng: String(lng),
      radiusKm: String(radiusKm),
      vehicleType,
    })
    return request<PoiResponse>(`/api/map/poi?${params}`)
  },
  poiBrands: (type: string, vehicleType: string) => {
    const params = new URLSearchParams({ type, vehicleType })
    return request<string[]>(`/api/map/poi/brands?${params}`)
  },
}
