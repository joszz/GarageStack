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

function mountSpot() {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  return mount(DashboardParkingSpot, {
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
  it("carries the map's OpenStreetMap credit, linked to its copyright page", () => {
    const wrapper = mountSpot()

    const credit = wrapper.get('a.parking-spot__credit')
    expect(credit.text()).toBe('© OpenStreetMap')
    expect(credit.attributes('href')).toBe('https://www.openstreetmap.org/copyright')
    expect(credit.attributes('target')).toBe('_blank')
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
