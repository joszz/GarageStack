import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import nl from '@/locales/nl.json'
import MapView from '../MapView.vue'
import { vehicleApi, type TelemetrySnapshot, type Trip, type Vehicle } from '@/services/vehicleApi'
import type { Place } from '@/services/mapApi'
import type { TripMatch } from '@/composables/useTripMatch'
import type { TripLayersOptions } from '@/composables/useTripLayers'
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
let tripLayerOptions: TripLayersOptions
vi.mock('@/composables/useTripLayers', () => ({
  useTripLayers: (o: TripLayersOptions) => {
    tripLayerOptions = o
    return tripLayers
  },
}))
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
const carPlace = ref<Place | null>(null)
vi.mock('@/composables/useReverseGeocode', () => ({
  useReverseGeocode: () => ({
    requestPlaces,
    placeFor: (_lat: number | undefined, _lng: number | undefined, precision: string) =>
      precision === 'address' ? carPlace.value : null,
    placeNamesEnabled: ref(false),
    placesResolving: ref(false),
  }),
}))

const popupUpdate = vi.fn<() => void>()
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
    LPopup: defineComponent({
      setup: (_, { slots, expose }) => {
        expose({ leafletObject: { update: popupUpdate } })
        return () => h('div', slots.default?.())
      },
    }),
  }
})

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en, nl } })

enableAutoUnmount(afterEach)

const car = { id: 1, vin: 'FAKEVN00000000001', vehicleType: 'hev', model: 'Kia Niro' } as Vehicle
const parked = {
  vehicleId: 1,
  latitude: 52.09,
  longitude: 5.12,
  currentJourneyDistance: 0,
  speed: 0,
  engineRunning: false,
  lastVehicleStateAt: '2026-10-19T10:00:00Z',
} as TelemetrySnapshot

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
      plugins: [i18n, pinia],
      stubs: {
        FontAwesomeIcon: true,
        Teleport: true,
        MapFilterPanel: true,
        MapLayerPanel: true,
        SpeedLegend: true,
        SpeedLimitLegend: true,
        RouterLink: {
          props: ['to'],
          template: '<a class="router-link" :data-to="JSON.stringify(to)"><slot /></a>',
        },
        TripSidebar: {
          props: ['rows', 'selectedIndex', 'activeIndex', 'shownDay'],
          emits: ['select', 'showPeriod'],
          template:
            '<ul class="sidebar" :data-selected="selectedIndex" :data-active="activeIndex" :data-day="shownDay">' +
            '<button v-if="shownDay" class="show-period" @click="$emit(\'showPeriod\')" />' +
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

// Handed to the mount and to every store the test reads, rather than left to the active pinia,
// which a busy test run has been seen to switch between a mount and its assertions.
let pinia: Pinia

describe('MapView', () => {
  beforeEach(() => {
    localStorage.clear()
    pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
    route.query = {}
    match.value = null
    carPlace.value = null
    i18n.global.locale.value = 'en'
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
    useUiSettingsStore(pinia).filterDays = 7
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
    pinia = createPinia()
    setActivePinia(pinia)
    vi.mocked(vehicleApi.status).mockResolvedValue({ ...parked, currentJourneyDistance: 4 })
    await mountView()
    expect(requestPlaces).not.toHaveBeenCalledWith(expect.anything(), 'address')
  })

  it('loads the trips again and drops the selection when the date filter changes', async () => {
    const wrapper = await mountView()
    await sidebar(wrapper).findAll('li')[0]!.trigger('click')
    vi.mocked(vehicleApi.trips).mockClear()

    useUiSettingsStore(pinia).filterDays = 90
    await flushPromises()

    expect(vehicleApi.trips).toHaveBeenCalledWith(car.vin, '2026-07-21T12:00:00.000Z', undefined)
    expect(sidebar(wrapper).attributes('data-selected')).toBeUndefined()
  })

  it('loads the trips again once a trip ends, keeping the selection', async () => {
    const wrapper = await mountView()
    await sidebar(wrapper).findAll('li')[0]!.trigger('click')
    vi.mocked(vehicleApi.trips).mockClear()

    useVehicleStore(pinia).notifyTripCompleted()
    await flushPromises()

    expect(vehicleApi.trips).toHaveBeenCalledOnce()
    expect(sidebar(wrapper).attributes('data-selected')).toBe('2')
  })

  it('shows the car on request, and offers it only while its position is known', async () => {
    const wrapper = await mountView()

    await findCar(wrapper).trigger('click')
    expect(tripLayers.showCar).toHaveBeenCalledWith([52.09, 5.12])

    pinia = createPinia()
    setActivePinia(pinia)
    vi.mocked(vehicleApi.status).mockResolvedValue({
      currentJourneyDistance: 0,
    } as TelemetrySnapshot)
    const unplaced = await mountView()
    expect(findCar(unplaced).attributes('disabled')).toBeDefined()
  })

  it("offers the same directions in the current car's popup", async () => {
    const wrapper = await mountView()
    expect(wrapper.find('.location-directions').exists()).toBe(false)
    expect(wrapper.get('.car-marker').findAll('button')).toHaveLength(1)
    await wrapper.get('.car-popup__directions').trigger('click')
    const popup = wrapper.get('.location-directions')
    const osm = popup.get('.location-directions__primary')

    expect(osm.text()).toBe(en.directions.osm)
    expect(new URL(osm.attributes('href')!).searchParams.get('to')).toBe('52.090000,5.120000')
    expect(popup.text()).toContain(en.directions.google)
    expect(popup.text()).toContain(en.directions.apple)
    expect(popup.text()).toContain(en.directions.mapApp)
  })

  it('updates the car popup after its address arrives', async () => {
    const wrapper = await mountView()
    popupUpdate.mockClear()
    let addressAtUpdate: string | undefined
    popupUpdate.mockImplementationOnce(() => {
      addressAtUpdate = wrapper.get('.car-popup__address').text()
    })
    carPlace.value = {
      road: 'A long test street',
      houseNumber: '55A',
      city: 'Amsterdam',
      postcode: null,
      countryCode: 'nl',
      displayName: null,
    }
    await flushPromises()

    expect(wrapper.get('.car-popup__address').text()).toBe('A long test street 55A, Amsterdam')
    expect(popupUpdate).toHaveBeenCalledOnce()
    expect(addressAtUpdate).toBe('A long test street 55A, Amsterdam')

    popupUpdate.mockClear()
    carPlace.value = null
    await flushPromises()
    expect(wrapper.find('.car-popup__address').exists()).toBe(false)
    expect(popupUpdate).toHaveBeenCalledOnce()
  })

  it('updates the car popup after its vehicle label or language changes', async () => {
    const wrapper = await mountView()
    popupUpdate.mockClear()
    const store = useVehicleStore(pinia)
    store.vehicles = [{ ...store.vehicles[0]!, model: 'A longer vehicle name' }]
    await flushPromises()
    expect(wrapper.get('.car-popup__title').text()).toBe('A longer vehicle name')
    expect(popupUpdate).toHaveBeenCalledOnce()

    popupUpdate.mockClear()
    i18n.global.locale.value = 'nl'
    await flushPromises()
    expect(wrapper.get('.car-popup__directions').text()).toBe(nl.directions.open)
    expect(popupUpdate).toHaveBeenCalledOnce()
  })

  it('closes the directions dialog without removing the car popup', async () => {
    const wrapper = await mountView()
    await wrapper.get('.car-popup__directions').trigger('click')
    await wrapper.get('.dialog-close').trigger('click')
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(wrapper.find('.car-popup__directions').exists()).toBe(true)
  })

  it('does not offer directions when the car may be moving, even without an active trip', async () => {
    vi.mocked(vehicleApi.status).mockResolvedValue({
      ...parked,
      speed: 15,
      engineRunning: true,
    })
    vi.mocked(vehicleApi.trips).mockResolvedValue([])
    const wrapper = await mountView()
    await wrapper.get('.car-popup__directions').trigger('click')
    const popup = wrapper.get('.location-directions')

    expect(popup.text()).toContain(en.directions.moving)
    expect(popup.get('button').attributes('disabled')).toBeDefined()
    expect(popup.find('a').exists()).toBe(false)
    expect(requestPlaces).not.toHaveBeenCalledWith(expect.anything(), 'address')
  })

  it('removes the old car marker until the selected car has its own valid coordinates', async () => {
    const wrapper = await mountView()
    const store = useVehicleStore(pinia)
    const otherCar = { ...car, id: 2, vin: 'FAKEVN00000000002' }

    store.vehicles = [otherCar]
    await flushPromises()
    expect(wrapper.find('.car-marker').exists()).toBe(false)
    expect(findCar(wrapper).attributes('disabled')).toBeDefined()

    store.currentStatus = { ...parked, vehicleId: 2, latitude: 51, longitude: 4 }
    await flushPromises()
    await wrapper.get('.car-popup__directions').trigger('click')
    const link = wrapper.get('.location-directions__primary')
    expect(new URL(link.attributes('href')!).searchParams.get('to')).toBe('51.000000,4.000000')

    store.currentStatus = { ...store.currentStatus, latitude: 91 }
    await flushPromises()
    expect(wrapper.find('.car-marker').exists()).toBe(false)
    expect(findCar(wrapper).attributes('disabled')).toBeDefined()
  })

  it('never turns an active trip endpoint into a directions destination', async () => {
    vi.mocked(vehicleApi.status).mockResolvedValue({
      ...parked,
      latitude: null,
      longitude: null,
      currentJourneyDistance: 4,
    })
    const wrapper = await mountView()

    expect(tripLayerOptions.activeCarLatLng.value).toEqual([52.1, 5.1])
    expect(wrapper.find('.location-directions').exists()).toBe(false)
    expect(findCar(wrapper).attributes('disabled')).toBeDefined()
  })

  it('offers the route editor on the map for a selected trip once it is saved', async () => {
    vi.mocked(vehicleApi.trips).mockResolvedValue([trip(0), { ...trip(1), id: null }])
    const wrapper = await mountView()
    const rows = () => sidebar(wrapper).findAll('li')
    const editLink = () => wrapper.find('.map-tools .router-link')

    expect(editLink().exists()).toBe(false)

    // The newest, still unsaved, has nothing to edit yet.
    await rows()[0]!.trigger('click')
    expect(wrapper.find('.map-tools').exists()).toBe(true)
    expect(editLink().exists()).toBe(false)

    await rows()[1]!.trigger('click')
    expect(JSON.parse(editLink().attributes('data-to')!)).toEqual({
      name: 'tripEdit',
      params: { id: 1 },
    })
    expect(editLink().attributes('aria-label')).toBe(en.trips.editRoute)
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

  it('selects the trip it was opened for from the trip log, once the trips are in', async () => {
    route.query = { trip: '2', at: trip(1).startedAt }
    useUiSettingsStore(pinia).filterDays = 30

    const wrapper = await mountView()

    expect(router.replace).toHaveBeenCalledWith({ name: 'map' })
    expect(vehicleApi.trips).toHaveBeenCalledWith(car.vin, '2026-09-19T12:00:00.000Z', undefined)
    expect(tripLayerOptions.takePendingSelection()).toBe(1)
    // Asked once: a later reload of the trips does not select it again.
    expect(tripLayerOptions.takePendingSelection()).toBeNull()
    expect(sidebar(wrapper).attributes('data-day')).toBeUndefined()
  })

  it('shows an older trip with the rest of its day, until the period is asked for again', async () => {
    route.query = { trip: '7', at: '2025-03-04T09:15:00Z' }
    const old = { ...trip(0), id: 7 }
    vi.mocked(vehicleApi.trips).mockResolvedValueOnce([old])

    const wrapper = await mountView()

    const [, from, to] = vi.mocked(vehicleApi.trips).mock.calls[0]!
    expect(new Date(from!).getTime()).toBeLessThanOrEqual(Date.parse('2025-03-04T09:15:00Z'))
    expect(new Date(to!).getTime() - new Date(from!).getTime()).toBe(86_400_000)
    expect(tripLayerOptions.takePendingSelection()).toBe(0)
    expect(sidebar(wrapper).attributes('data-day')).toBeTruthy()

    await wrapper.find('.show-period').trigger('click')
    await flushPromises()

    expect(vehicleApi.trips).toHaveBeenLastCalledWith(
      car.vin,
      '2026-10-12T12:00:00.000Z',
      undefined,
    )
    expect(sidebar(wrapper).attributes('data-day')).toBeUndefined()
  })
})
