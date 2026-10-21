import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, nextTick, ref, shallowRef } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import type { LeafletMap } from '@/utils/leaflet'
import type { TelemetrySnapshot, Trip, TripPoint } from '@/services/vehicleApi'
import type { TripMatch } from '../useTripMatch'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { fitToPoints } from '@/utils/leaflet'
import { moveCarMarker } from '@/utils/mapCarIcon'
import { useTripLayers, type TripLayersOptions } from '../useTripLayers'

// Leaflet is a recorder here: every line and marker on the map is a plain object in `drawn`, so a
// test reads the picture off it rather than off a canvas.
interface Drawn {
  kind: 'line' | 'marker' | 'heat'
  latLngs: unknown
  options: Record<string, unknown>
  click?: () => void
}
const drawn: Drawn[] = []

function layer(kind: Drawn['kind'], latLngs: unknown, options: Record<string, unknown> = {}) {
  const entry: Drawn = { kind, latLngs, options }
  return {
    entry,
    on(_event: string, handler: () => void) {
      entry.click = handler
      return this
    },
    addTo() {
      drawn.push(entry)
      return this
    },
    remove() {
      drawn.splice(drawn.indexOf(entry), 1)
    },
  }
}

vi.mock('@/utils/leaflet', () => ({
  L: {
    polyline: (latLngs: unknown, options: Record<string, unknown>) =>
      layer('line', latLngs, options),
    marker: (latLng: unknown, options: { icon: { html?: string } }) =>
      layer('marker', latLng, { html: options.icon.html }),
    divIcon: (options: { html: string }) => options,
  },
  fitToPoints: vi.fn<typeof import('@/utils/leaflet').fitToPoints>(),
}))
vi.mock('@/utils/heatLayer', () => ({ heatLayer: () => layer('heat', null) }))
vi.mock('@/utils/mapCarIcon', () => ({
  buildCarMarkerIcon: () => ({ html: 'car' }),
  moveCarMarker: vi.fn<typeof import('@/utils/mapCarIcon').moveCarMarker>(),
}))

const lines = () => drawn.filter((d) => d.kind === 'line')
const markers = () => drawn.filter((d) => d.kind === 'marker').map((d) => d.options.html)
const heat = () => drawn.some((d) => d.kind === 'heat')

function points(...speeds: (number | null)[]): TripPoint[] {
  return speeds.map((speed, i) => ({
    recordedAt: `2026-10-19T07:0${i}:00Z`,
    latitude: 52 + i / 100,
    longitude: 5 + i / 100,
    speed,
  }))
}

function trip(index: number, pts: TripPoint[]): Trip {
  return { index, id: index + 1, points: pts } as Trip
}

const twoTrips = () => [trip(0, points(30, 50, 70)), trip(1, points(80, 100))]

function setup(overrides: Partial<TripLayersOptions> = {}) {
  const map = { setView: vi.fn<LeafletMap['setView']>() }
  const o = {
    map: shallowRef<LeafletMap | null>(map as unknown as LeafletMap),
    trips: ref<Trip[]>(twoTrips()),
    selectedIndex: ref<number | null>(null),
    activeIndex: ref<number | null>(null),
    status: ref<TelemetrySnapshot | null>(null),
    activeCarLatLng: ref<[number, number] | null>(null),
    selectedMatch: shallowRef<TripMatch | null>(null),
    limitColoursShown: ref(false),
    heatPoints: ref<[number, number][]>([[52, 5]]),
    onSelect: vi.fn<(index: number) => void>(),
    takePendingSelection: () => null,
    ...overrides,
  }
  let api!: ReturnType<typeof useTripLayers>
  const wrapper = mount(
    defineComponent({
      setup() {
        api = useTripLayers(o)
        return () => null
      },
    }),
    { global: { plugins: [pinia] } },
  )
  return { o, map, api, wrapper }
}

// Handed to the mount and to every store the test reads, rather than left to the active pinia,
// which a busy test run has been seen to switch between a mount and its assertions.
let pinia: Pinia

describe('useTripLayers', () => {
  beforeEach(() => {
    drawn.length = 0
    localStorage.clear()
    pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
    // The redraws wait for the next frame; here that frame comes at once.
    vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
      cb(0)
      return 1
    })
    vi.stubGlobal('cancelAnimationFrame', () => {})
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('draws every trip over the heatmap and frames them all', () => {
    const { api } = setup()

    api.drawInitial()

    expect(heat()).toBe(true)
    expect(lines()).toHaveLength(2)
    expect(vi.mocked(fitToPoints).mock.calls[0]![1]).toHaveLength(5)
  })

  it('leaves out a trip with a single fix, which makes no line', () => {
    const { o, api } = setup()
    o.trips.value = [...twoTrips(), trip(2, points(0))]

    api.drawInitial()

    expect(lines()).toHaveLength(2)
  })

  it('selects a trip when its line is clicked', () => {
    const { o, api } = setup()
    api.drawInitial()

    lines()[1]!.click!()

    expect(o.onSelect).toHaveBeenCalledWith(1)
  })

  it('outlines every line when the route outline is on', () => {
    useMapSettingsStore(pinia).routeOutlineEnabled = true
    const { api } = setup()

    api.drawInitial()

    expect(lines()).toHaveLength(4)
  })

  it('centres on the car once when there are no trips, and stays put after that', async () => {
    const { o, map, api } = setup({ trips: ref([]) })
    api.drawInitial()
    expect(map.setView).not.toHaveBeenCalled()

    o.status.value = { latitude: 52.1, longitude: 5.1 } as TelemetrySnapshot
    await nextTick()
    o.status.value = { latitude: 52.2, longitude: 5.2 } as TelemetrySnapshot
    await nextTick()

    expect(map.setView).toHaveBeenCalledExactlyOnceWith([52.1, 5.1], 14, { animate: false })
  })

  it('draws a selected trip on its own, from start to finish, without the heatmap', async () => {
    const { o, api } = setup()
    api.drawInitial()
    vi.mocked(fitToPoints).mockClear()

    o.selectedIndex.value = 0
    await nextTick()

    expect(heat()).toBe(false)
    expect(lines()).toHaveLength(1)
    expect(markers()).toEqual([
      expect.stringContaining('trip-marker--start'),
      expect.stringContaining('trip-marker--end'),
    ])
    expect(vi.mocked(fitToPoints).mock.calls[0]![1]).toHaveLength(3)
  })

  it('ends the trip being driven at the car, which follows the live position', async () => {
    const { o } = setup({ activeIndex: ref(1), activeCarLatLng: ref([52.5, 5.5]) })

    o.selectedIndex.value = 1
    await nextTick()
    expect(markers()).toEqual([expect.stringContaining('trip-marker--start'), 'car'])

    o.status.value = { latitude: 52.6, longitude: 5.6, heading: 90 } as TelemetrySnapshot
    await nextTick()
    expect(moveCarMarker).toHaveBeenCalledWith(expect.anything(), [52.6, 5.6], 90)
  })

  it('colours the selected trip by speed, one line per stretch between fixes', async () => {
    useMapSettingsStore(pinia).speedOverlayEnabled = true
    const { o } = setup()

    o.selectedIndex.value = 0
    await nextTick()

    expect(lines()).toHaveLength(2)
    expect(new Set(lines().map((l) => l.options.color)).size).toBe(2)
  })

  it('colours the snapped trip against the speed limits, and switches the speed colours off for it', async () => {
    const settings = useMapSettingsStore(pinia)
    settings.speedOverlayEnabled = true
    const { o } = setup()
    o.selectedIndex.value = 0
    await nextTick()

    settings.speedLimitOverlayEnabled = true
    o.limitColoursShown.value = true
    o.selectedMatch.value = {
      coordinates: [
        [52, 5],
        [52.01, 5.01],
        [52.02, 5.02],
      ],
      points: o.trips.value[0]!.points,
      pointIndexes: [0, 1, 2],
      matchedKm: 3,
      speedLimits: [50, 30],
    }
    await flushPromises()

    expect(settings.speedOverlayEnabled).toBe(false)
    expect(lines().map((l) => l.options.className)).toEqual([
      'trip-limit-line trip-limit-line--under',
      'trip-limit-line trip-limit-line--over',
    ])
  })

  it('draws every trip again once the selection is dropped', async () => {
    const { o } = setup()
    o.selectedIndex.value = 0
    await nextTick()

    o.selectedIndex.value = null
    await nextTick()

    expect(heat()).toBe(true)
    expect(lines()).toHaveLength(2)
    expect(markers()).toEqual([])
  })

  it('shows the car rather than every trip when asked for it from a selected trip', async () => {
    const { o, map, api } = setup()
    o.selectedIndex.value = 0
    await nextTick()
    vi.mocked(fitToPoints).mockClear()

    api.showCar([52.3, 4.9])
    await nextTick()

    expect(o.selectedIndex.value).toBeNull()
    expect(map.setView).toHaveBeenCalledWith([52.3, 4.9], 14, { animate: false })
    expect(fitToPoints).not.toHaveBeenCalled()
  })

  it('selects the trip the view was opened for when the trips arrive', async () => {
    const onSelect = vi.fn<(index: number) => void>()
    const { o } = setup({ trips: ref([]), onSelect, takePendingSelection: () => 1 })

    o.trips.value = twoTrips()
    await flushPromises()

    expect(onSelect).toHaveBeenCalledWith(1)
  })

  it('keeps the trip it was opened for on its own when the map is laid out after the trips arrived', async () => {
    // The trips land while the map is still waiting for its first layout; the first picture
    // then comes after the trip has been selected, and must not draw every trip over it.
    let pending: number | null = 1
    const { o, api } = setup({
      trips: ref([]),
      takePendingSelection: () => {
        const take = pending
        pending = null
        return take
      },
    })
    o.onSelect = vi.fn<(idx: number) => void>((idx) => (o.selectedIndex.value = idx))

    o.trips.value = twoTrips()
    await flushPromises()
    vi.mocked(fitToPoints).mockClear()
    api.drawInitial()

    expect(o.selectedIndex.value).toBe(1)
    expect(heat()).toBe(false)
    expect(lines()).toHaveLength(1)
    const frames = vi.mocked(fitToPoints).mock.calls
    expect(frames[frames.length - 1]![1]).toHaveLength(2)
  })

  it('keeps a selected trip on its own, where it is, when the trips are loaded again', async () => {
    const { o } = setup()
    o.selectedIndex.value = 0
    await nextTick()
    vi.mocked(fitToPoints).mockClear()

    o.trips.value = [...twoTrips(), trip(2, points(10, 20))]
    await flushPromises()

    expect(heat()).toBe(false)
    expect(lines()).toHaveLength(1)
    expect(fitToPoints).not.toHaveBeenCalled()
  })
})
