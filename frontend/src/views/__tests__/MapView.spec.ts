import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import MapView from '../MapView.vue'
import { vehicleApi, type TelemetrySnapshot, type Trip, type Vehicle } from '@/services/vehicleApi'
import type { TripMatch } from '@/composables/useTripMatch'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: {
    list: vi.fn<() => Promise<Vehicle[]>>(),
    status: vi.fn<() => Promise<TelemetrySnapshot | null>>(),
    config: vi.fn<() => Promise<Record<string, string>>>(),
    trips: vi.fn<() => Promise<Trip[]>>(),
  },
}))

const route = { query: {} as Record<string, string> }
const router = { replace: vi.fn<(to: { name: string }) => void>() }
vi.mock('vue-router', () => ({ useRoute: () => route, useRouter: () => router }))

// The map and its layers have their own suites (useTripLayers, usePoiLayers); here they only have
// to say what the view asked of them.
const tripLayers = {
  drawInitial: vi.fn<() => void>(),
  showCar: vi.fn<(latLng: [number, number]) => void>(),
}
vi.mock('@/composables/useTripLayers', () => ({ useTripLayers: () => tripLayers }))
vi.mock('@/composables/usePoiLayers', () => ({
  usePoiLayers: () => ({
    speedCamerasAvailable: ref(true),
    availableFuelBrands: ref([]),
    brandsLoading: ref(false),
    poiLoading: ref(false),
    loadLayers: vi.fn<(radiusKm?: number) => void>(),
  }),
}))
vi.mock('@/composables/useBasemap', () => ({ useBasemap: () => undefined }))

const match = ref<TripMatch | null>(null)
const requestMatch = vi.fn<(trip: Trip | null) => void>()
vi.mock('@/composables/useTripMatch', () => ({
  useTripMatch: () => ({
    requestMatch,
    matchFor: (trip: Trip | null) => (trip ? match.value : null),
    snapEnabled: ref(true),
    matching: ref(false),
  }),
}))

const requestPlaces = vi.fn<(points: unknown[], detail: string) => void>()
vi.mock('@/composables/useReverseGeocode', () => ({
  useReverseGeocode: () => ({
    requestPlaces,
    placeFor: () => null,
    placeNamesEnabled: ref(false),
    placesResolving: ref(false),
  }),
}))

vi.mock('@vue-leaflet/vue-leaflet', () => {
  const container = defineComponent({
    setup:
      (_, { slots }) =>
      () =>
        h('div', slots.default?.()),
  })
  return {
    LMap: container,
    LMarker: defineComponent({
      setup:
        (_, { slots }) =>
        () =>
          h('div', { class: 'car-marker' }, slots.default?.()),
    }),
    LPopup: container,
  }
})

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

enableAutoUnmount(afterEach)

const car = { id: 1, vin: 'FAKEVN00000000001', vehicleType: 'hev', model: 'Kia Niro' } as Vehicle
const parked = { latitude: 52.09, longitude: 5.12, currentJourneyDistance: 0 } as TelemetrySnapshot

function trip(index: number): Trip {
  const at = (minute: number) => `2026-10-${10 + index}T07:${minute}:00Z`
  return {
    index,
    id: index + 1,
    startedAt: at(0),
    endedAt: at(30),
    distanceKm: 10 + index,
    pointCount: 2,
    endLatitude: 52.1,
    endLongitude: 5.1,
    maxSpeedKmh: null,
    avgMovingSpeedKmh: null,
    movingSpeedSamples: 0,
    points: [
      { recordedAt: at(0), latitude: 52, longitude: 5, speed: null },
      { recordedAt: at(30), latitude: 52.1, longitude: 5.1, speed: null },
    ],
  }
}

async function mountView() {
  const wrapper = mount(MapView, {
    global: {
      plugins: [i18n],
      stubs: {
        FontAwesomeIcon: true,
        MapFilterPanel: true,
        MapLayerPanel: true,
        SpeedLegend: true,
        SpeedLimitLegend: true,
        TripSidebar: {
          props: ['rows', 'selectedIndex', 'activeIndex'],
          emits: ['select'],
          template:
            '<ul class="sidebar" :data-selected="selectedIndex" :data-active="activeIndex">' +
            '<li v-for="r in rows" :key="r.realIdx" @click="$emit(\'select\', r.realIdx)">{{ r.realIdx }}</li></ul>',
        },
      },
    },
  })
  await flushPromises()
  return wrapper
}

const sidebar = (wrapper: Awaited<ReturnType<typeof mountView>>) => wrapper.find('.sidebar')
const findCar = (wrapper: Awaited<ReturnType<typeof mountView>>) =>
  wrapper.findAll('button').find((b) => b.text() === en.trips.findCar)!

describe('MapView', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.clearAllMocks()
    route.query = {}
    match.value = null
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-19T12:00:00Z'))
    vi.mocked(vehicleApi.list).mockResolvedValue([car])
    vi.mocked(vehicleApi.status).mockResolvedValue(parked)
    vi.mocked(vehicleApi.config).mockResolvedValue({})
    vi.mocked(vehicleApi.trips).mockResolvedValue([trip(0), trip(1), trip(2)])
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it("loads the car's position and the trips of the date filter's days", async () => {
    useUiSettingsStore().filterDays = 7
    await mountView()

    expect(vehicleApi.status).toHaveBeenCalledWith(car.vin)
    expect(vehicleApi.trips).toHaveBeenCalledWith(car.vin, '2026-10-12T12:00:00.000Z', undefined)
  })

  it('lists the trips newest first, and selects one on a tap and drops it on the next', async () => {
    const wrapper = await mountView()
    const rows = () => sidebar(wrapper).findAll('li')

    expect(rows().map((r) => r.text())).toEqual(['2', '1', '0'])

    await rows()[1]!.trigger('click')
    expect(sidebar(wrapper).attributes('data-selected')).toBe('1')
    // The car's marker makes way for the selected trip's own ends.
    expect(wrapper.find('.car-marker').exists()).toBe(false)

    await rows()[1]!.trigger('click')
    expect(sidebar(wrapper).attributes('data-selected')).toBeUndefined()
    expect(wrapper.find('.car-marker').exists()).toBe(true)
  })

  it('marks the newest trip as the one being driven while the car is on its way', async () => {
    vi.mocked(vehicleApi.status).mockResolvedValue({ ...parked, currentJourneyDistance: 4 })

    const wrapper = await mountView()

    expect(sidebar(wrapper).attributes('data-active')).toBe('2')
  })

  it('looks up the street of a parked car, but not of one on its way', async () => {
    await mountView()
    expect(requestPlaces).toHaveBeenCalledWith([{ lat: 52.09, lng: 5.12 }], 'address')

    requestPlaces.mockClear()
    setActivePinia(createPinia())
    vi.mocked(vehicleApi.status).mockResolvedValue({ ...parked, currentJourneyDistance: 4 })
    await mountView()
    expect(requestPlaces).not.toHaveBeenCalledWith(expect.anything(), 'address')
  })

  it('loads the trips again and drops the selection when the date filter changes', async () => {
    const wrapper = await mountView()
    await sidebar(wrapper).findAll('li')[0]!.trigger('click')
    vi.mocked(vehicleApi.trips).mockClear()

    useUiSettingsStore().filterDays = 90
    await flushPromises()

    expect(vehicleApi.trips).toHaveBeenCalledWith(car.vin, '2026-07-21T12:00:00.000Z', undefined)
    expect(sidebar(wrapper).attributes('data-selected')).toBeUndefined()
  })

  it('loads the trips again once a trip ends, keeping the selection', async () => {
    const wrapper = await mountView()
    await sidebar(wrapper).findAll('li')[0]!.trigger('click')
    vi.mocked(vehicleApi.trips).mockClear()

    useVehicleStore().notifyTripCompleted()
    await flushPromises()

    expect(vehicleApi.trips).toHaveBeenCalledOnce()
    expect(sidebar(wrapper).attributes('data-selected')).toBe('2')
  })

  it('shows the car on request, and offers it only while its position is known', async () => {
    const wrapper = await mountView()

    await findCar(wrapper).trigger('click')
    expect(tripLayers.showCar).toHaveBeenCalledWith([52.09, 5.12])

    setActivePinia(createPinia())
    vi.mocked(vehicleApi.status).mockResolvedValue({
      currentJourneyDistance: 0,
    } as TelemetrySnapshot)
    const unplaced = await mountView()
    expect(findCar(unplaced).attributes('disabled')).toBeDefined()
  })

  it('snaps the selected trip, and says how long it turned out to be', async () => {
    const wrapper = await mountView()
    match.value = {
      coordinates: [],
      points: [],
      pointIndexes: [],
      matchedKm: 12.4,
      speedLimits: [],
    }

    await sidebar(wrapper).findAll('li')[0]!.trigger('click')

    expect(requestMatch).toHaveBeenLastCalledWith(expect.objectContaining({ index: 2 }))
    expect(wrapper.find('.map-pill').text()).toBe('Snapped · 12.4 km')
  })

  it('takes the request to open on the newest trip off the address once it has it', async () => {
    route.query = { selectLatest: '1' }

    await mountView()

    expect(router.replace).toHaveBeenCalledWith({ name: 'map' })
  })
})
