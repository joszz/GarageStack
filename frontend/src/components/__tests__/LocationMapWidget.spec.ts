import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { TelemetrySnapshot, Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import LocationMapWidget from '../LocationMapWidget.vue'

const push = vi.fn<(to: { name: string; query?: Record<string, string> }) => void>()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@vue-leaflet/vue-leaflet', () => {
  const container = defineComponent({
    setup:
      (_, { slots }) =>
      () =>
        h('div', slots.default?.()),
  })
  return { LMap: container, LMarker: container }
})
vi.mock('@/composables/useBasemap', () => ({ useBasemap: () => undefined }))

const requestPlaces = vi.fn<(points: { lat: number; lng: number }[], precision: string) => void>()
vi.mock('@/composables/useReverseGeocode', () => ({
  useReverseGeocode: () => ({
    requestPlaces,
    placeFor: () => null,
    placeNamesEnabled: ref(false),
    placesResolving: ref(false),
  }),
}))

enableAutoUnmount(afterEach)

const car = { id: 1, vin: 'FAKEVN00000000001', model: 'MG' } as Vehicle
const status = {
  vehicleId: 1,
  latitude: 52.09,
  longitude: 5.12,
  currentJourneyDistance: 0,
  speed: 0,
  engineRunning: false,
  lastVehicleStateAt: null,
} as TelemetrySnapshot

let pinia: Pinia

function mountWidget() {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  return mount(LocationMapWidget, {
    global: {
      plugins: [pinia, i18n],
      stubs: {
        FontAwesomeIcon: true,
        Teleport: true,
        CardInfoWrap: { template: '<div><slot /></div>' },
      },
    },
  })
}

describe('LocationMapWidget', () => {
  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    const store = useVehicleStore(pinia)
    store.vehicles = [car]
    store.currentStatus = status
    vi.clearAllMocks()
  })

  it('keeps the preview and expand button opening the full map independently of directions', async () => {
    const wrapper = mountWidget()
    expect(wrapper.find('.location-directions').exists()).toBe(false)
    await wrapper.get('.location-map-card__directions').trigger('click')
    expect(wrapper.get('[role="dialog"]').attributes('aria-label')).toBe(en.directions.open)
    const osm = wrapper.get('.location-directions__primary')
    expect(new URL(osm.attributes('href')!).searchParams.get('to')).toBe('52.090000,5.120000')

    expect(push).not.toHaveBeenCalled()
    await wrapper.get('.dialog-close').trigger('click')
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    await wrapper.get('.location-map-card__map').trigger('click')
    await wrapper.get('.location-map-card__expand').trigger('click')
    expect(push).toHaveBeenCalledTimes(2)
    expect(push).toHaveBeenNthCalledWith(1, { name: 'map' })
    expect(push).toHaveBeenNthCalledWith(2, { name: 'map' })
  })

  it('does not expose a previous vehicle destination while the selected vehicle changes', async () => {
    const wrapper = mountWidget()
    await wrapper.get('.location-map-card__directions').trigger('click')
    const store = useVehicleStore(pinia)
    store.vehicles = [{ ...car, id: 2, vin: 'FAKEVN00000000002' }]
    await flushPromises()

    expect(wrapper.get('.location-map-card__empty').text()).toContain(
      en.vehicle.locationUnavailable,
    )
    expect(wrapper.get('.location-directions button').attributes('disabled')).toBeDefined()
    expect(wrapper.find('.location-directions a').exists()).toBe(false)

    store.currentStatus = { ...status, vehicleId: 2, latitude: 51, longitude: 4 }
    await flushPromises()
    expect(
      new URL(wrapper.get('.location-directions__primary').attributes('href')!).searchParams.get(
        'to',
      ),
    ).toBe('51.000000,4.000000')
  })

  it('disables directions while a trip appears active even if the latest trip is not loaded', async () => {
    const wrapper = mountWidget()
    await wrapper.get('.location-map-card__directions').trigger('click')
    const store = useVehicleStore(pinia)
    requestPlaces.mockClear()
    store.currentStatus = { ...status, currentJourneyDistance: 4 }
    await flushPromises()

    expect(wrapper.text()).toContain(en.directions.moving)
    expect(wrapper.get('.location-directions button').attributes('disabled')).toBeDefined()
    expect(wrapper.find('.location-directions a').exists()).toBe(false)
    expect(requestPlaces).not.toHaveBeenCalled()
  })

  it('shows unavailable for invalid coordinates but accepts zero coordinates', async () => {
    const wrapper = mountWidget()
    await wrapper.get('.location-map-card__directions').trigger('click')
    const store = useVehicleStore(pinia)
    store.currentStatus = { ...status, latitude: NaN }
    await flushPromises()
    expect(wrapper.find('.location-directions a').exists()).toBe(false)

    store.currentStatus = { ...status, latitude: 0, longitude: 0 }
    await flushPromises()
    expect(
      new URL(wrapper.get('.location-directions__primary').attributes('href')!).searchParams.get(
        'to',
      ),
    ).toBe('0.000000,0.000000')
  })
})
