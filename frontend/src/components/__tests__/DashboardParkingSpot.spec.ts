import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { Place } from '@/services/mapApi'
import type { TelemetrySnapshot, Trip, Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import DashboardParkingSpot from '../DashboardParkingSpot.vue'

const push = vi.fn<(to: { name: string; query?: Record<string, string> }) => void>()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

// The street the geocoder has named for the car's spot, if any.
const geocoded = vi.hoisted(() => ({ place: null as Place | null }))
const requestPlaces = vi.fn<(points: { lat: number; lng: number }[], precision: string) => void>()
vi.mock('@/composables/useReverseGeocode', () => ({
  useReverseGeocode: () => ({
    requestPlaces,
    placeFor: () => geocoded.place,
    placeNamesEnabled: ref(true),
    placesResolving: ref(false),
  }),
}))

enableAutoUnmount(afterEach)

const car = { id: 1, vin: 'FAKEVN00000000001', model: 'MG' } as Vehicle
const parked = {
  vehicleId: 1,
  latitude: 52.09,
  longitude: 5.12,
  currentJourneyDistance: 0,
  speed: 0,
  engineRunning: false,
  lastVehicleStateAt: null,
} as TelemetrySnapshot

let pinia: Pinia

function mountSpot(basemap: 'vector' | 'raster' | null = 'vector') {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  return mount(DashboardParkingSpot, {
    props: { basemap },
    global: { plugins: [pinia, i18n], stubs: { FontAwesomeIcon: true, Teleport: true } },
  })
}

describe('DashboardParkingSpot', () => {
  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    const store = useVehicleStore(pinia)
    store.vehicles = [car]
    store.currentStatus = parked
    geocoded.place = null
    vi.clearAllMocks()
  })

  it('names the street the car is parked in', () => {
    geocoded.place = {
      road: 'Grote Markt',
      houseNumber: '1',
      city: 'Zwolle',
      postcode: null,
      countryCode: 'nl',
      displayName: null,
    }
    const wrapper = mountSpot()

    expect(requestPlaces).toHaveBeenCalledWith([{ lat: 52.09, lng: 5.12 }], 'address')
    expect(wrapper.get('.parking-spot__label').text()).toBe('Grote Markt 1, Zwolle')
  })

  it('asks for no address while the car drives, and says only that this is its location', () => {
    useVehicleStore(pinia).currentStatus = { ...parked, currentJourneyDistance: 3 }
    useVehicleStore(pinia).latestTrip = { points: [] } as unknown as Trip
    const wrapper = mountSpot()

    expect(requestPlaces).not.toHaveBeenCalled()
    expect(wrapper.get('.parking-spot__label').text()).toBe(en.vehicle.location)
  })

  it('says so when the car has no position', async () => {
    useVehicleStore(pinia).currentStatus = { ...parked, latitude: null, longitude: null }
    const wrapper = mountSpot()
    await flushPromises()

    expect(wrapper.get('.parking-spot__label').text()).toBe(en.vehicle.locationUnavailable)
  })

  // The map behind the cards cannot show it: its corners lie under them.
  it("carries the vector basemap's credit, each part linked", () => {
    const wrapper = mountSpot('vector')

    const links = wrapper.findAll('.parking-spot__credit a')
    expect(links.map((link) => link.text())).toEqual(['© OpenMapTiles', '© OpenStreetMap'])
    expect(links.map((link) => link.attributes('href'))).toEqual([
      'https://openmaptiles.org',
      'https://www.openstreetmap.org/copyright',
    ])
    expect(links.every((link) => link.attributes('target') === '_blank')).toBe(true)
    expect(wrapper.get('.parking-spot__credit').text()).toBe('© OpenMapTiles · © OpenStreetMap')
  })

  it('credits OpenStreetMap alone when the map is on the raster fallback', () => {
    const wrapper = mountSpot('raster')

    expect(wrapper.get('.parking-spot__credit').text()).toBe('© OpenStreetMap')
  })

  it('credits nothing before the map has a basemap', () => {
    const wrapper = mountSpot(null)

    expect(wrapper.find('.parking-spot__credit').exists()).toBe(false)
  })

  it('offers directions and the full map, as the location card does', async () => {
    const wrapper = mountSpot()

    await wrapper.get('.location-map-card__directions').trigger('click')
    const osm = wrapper.get('.location-directions__primary')
    expect(new URL(osm.attributes('href')!).searchParams.get('to')).toBe('52.090000,5.120000')

    await wrapper.get('.location-map-card__expand').trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'map' })
  })
})
