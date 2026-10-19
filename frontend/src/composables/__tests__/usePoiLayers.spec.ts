import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { computed, defineComponent, nextTick, ref, shallowRef } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { LeafletMap } from '@/utils/leaflet'
import type { VehicleType } from '@/stores/vehicle'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { mapApi, type ChargingStation, type PoiItem } from '@/services/mapApi'
import type { TileLayerOptions } from '../poiTileLayer'
import { usePoiLayers } from '../usePoiLayers'

// The tile layers' fetching and drawing has its own suite (poiTileLayer.spec.ts). Here each layer
// is a stand-in that keeps the options it was built with, which is where this composable's rules
// live: when a layer applies, what its filters keep, and what its popups say.
interface FakeLayer {
  options: TileLayerOptions<never>
  items: Map<string, unknown>
  load: Mock<(radiusKm?: number) => void>
  scheduleLoad: Mock<(delayMs: number) => void>
  redraw: Mock<() => void>
  dispose: Mock<() => void>
}
const layers: FakeLayer[] = []

vi.mock('../poiTileLayer', async (importOriginal) => ({
  escapeHtml: (await importOriginal<typeof import('../poiTileLayer')>()).escapeHtml,
  createMarker: (
    _lat: number,
    _lng: number,
    className: string,
    _glyph: string,
    popup: () => string,
  ) => ({
    className,
    popup,
  }),
  createTileLayer: (options: TileLayerOptions<never>) => {
    const layer = {
      options,
      items: new Map(),
      load: vi.fn<(radiusKm?: number) => void>(),
      scheduleLoad: vi.fn<(delayMs: number) => void>(),
      redraw: vi.fn<() => void>(),
      dispose: vi.fn<() => void>(),
    }
    layers.push(layer)
    return layer
  },
}))

vi.mock('../useLayerCredit', () => ({ useLayerCredit: () => undefined }))

vi.mock('@/services/mapApi', () => ({
  mapApi: {
    chargingStations: vi.fn<typeof mapApi.chargingStations>(),
    poi: vi.fn<typeof mapApi.poi>(),
    poiBrands: vi.fn<typeof mapApi.poiBrands>(),
  },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

// In the order the composable builds them.
const charging = () =>
  layers[0]! as unknown as FakeLayer & { options: TileLayerOptions<ChargingStation> }
const fuel = () => layers[1]! as unknown as FakeLayer & { options: TileLayerOptions<PoiItem> }
const speedCameras = () =>
  layers[3]! as unknown as FakeLayer & { options: TileLayerOptions<PoiItem> }

function station(kw: (number | null)[], overrides: Partial<ChargingStation> = {}): ChargingStation {
  return {
    id: 1,
    title: 'Laadplein',
    latitude: 52,
    longitude: 5,
    operator: null,
    addressLine: null,
    town: null,
    numberOfPoints: null,
    isOperational: true,
    connectors: kw.map((powerKw) => ({ type: 'Type 2', powerKw, quantity: 1 })),
    ...overrides,
  } as ChargingStation
}

function poi(tags: Record<string, string>, overrides: Partial<PoiItem> = {}): PoiItem {
  return {
    externalId: 'node/1',
    poiType: 'fuel',
    latitude: 52,
    longitude: 5,
    name: 'Tankstation',
    tags,
    ...overrides,
  } as PoiItem
}

function fakeMap() {
  const handlers = new Map<string, () => void>()
  return {
    handlers,
    on: vi.fn<(events: string, handler: () => void) => void>((events, handler) => {
      handlers.set(events, handler)
    }),
    off: vi.fn<(events: string, handler: () => void) => void>((events) => {
      handlers.delete(events)
    }),
  }
}

function setup(type: VehicleType, map: ReturnType<typeof fakeMap> | null = fakeMap()) {
  const vehicleType = ref<VehicleType>(type)
  const mapInstance = shallowRef<LeafletMap | null>(null)
  let result!: ReturnType<typeof usePoiLayers>
  const wrapper = mount(
    defineComponent({
      setup() {
        result = usePoiLayers({ mapInstance, vehicleType: computed(() => vehicleType.value) })
        return () => null
      },
    }),
    { global: { plugins: [i18n] } },
  )
  mapInstance.value = map as unknown as LeafletMap
  return { wrapper, result, vehicleType, mapInstance, map }
}

describe('usePoiLayers', () => {
  beforeEach(() => {
    layers.length = 0
    localStorage.clear()
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.mocked(mapApi.poiBrands).mockResolvedValue([])
  })

  it('offers charging stations only to a car with a plug, and fuel stations only to one with a tank', () => {
    const settings = useMapSettingsStore()
    settings.chargingStationsEnabled = true
    settings.fuelStationsEnabled = true

    setup('hev')
    expect(charging().options.enabled.value).toBe(false)
    expect(fuel().options.enabled.value).toBe(true)

    layers.length = 0
    setup('bev')
    expect(charging().options.enabled.value).toBe(true)
    expect(fuel().options.enabled.value).toBe(false)
  })

  it('keeps the charging stations within the power range, and redraws when it changes', async () => {
    const settings = useMapSettingsStore()
    setup('bev')
    const keeps = (s: ChargingStation) => charging().options.include!(s)

    settings.chargingMinPowerKw = 50
    settings.chargingMaxPowerKw = 150
    await nextTick()

    expect(keeps(station([11, 22]))).toBe(false)
    expect(keeps(station([11, 100]))).toBe(true)
    expect(keeps(station([350]))).toBe(false)
    // An unknown power cannot be ruled out by the upper bound.
    expect(keeps(station([null, 50]))).toBe(true)
    expect(charging().redraw).toHaveBeenCalled()
  })

  it('keeps the fuel stations that pass both the brand and the fuel type, and redraws when either changes', async () => {
    const settings = useMapSettingsStore()
    setup('hev')
    const keeps = (item: PoiItem) => fuel().options.include!(item)

    settings.fuelBrandFilter = ['BP']
    settings.fuelTypeFilter = ['diesel']
    await nextTick()

    // A brand variant counts as its brand.
    expect(keeps(poi({ brand: 'BP Express', 'fuel:diesel': 'yes' }))).toBe(true)
    expect(keeps(poi({ brand: 'BP', 'fuel:diesel': 'no', 'fuel:octane_95': 'yes' }))).toBe(false)
    expect(keeps(poi({ brand: 'Shell', 'fuel:diesel': 'yes' }))).toBe(false)
    expect(keeps(poi({ 'fuel:diesel': 'yes' }))).toBe(false)
    expect(fuel().redraw).toHaveBeenCalled()
  })

  it("loads the brand list for a car that burns fuel, and offers it with the stations' own brands", async () => {
    vi.mocked(mapApi.poiBrands).mockResolvedValue(['Shell', 'BP Express'])
    const { result } = setup('phev')
    await flushPromises()
    fuel().items.set('node/2', poi({ brand: 'Tango' }))
    fuel().items.set('node/3', poi({ brand: 'BP' }))

    expect(mapApi.poiBrands).toHaveBeenCalledWith('fuel', 'phev')
    expect(result.availableFuelBrands.value).toEqual(['BP', 'Shell', 'Tango'])
  })

  it('loads no brand list for a car that burns none', async () => {
    setup('bev')
    await flushPromises()

    expect(mapApi.poiBrands).not.toHaveBeenCalled()
  })

  it('waits for the vehicle type before loading fuel stations, and loads them once it is known', async () => {
    const { vehicleType } = setup('unknown')
    expect(fuel().options.ready!()).toBe(false)

    vehicleType.value = 'hev'
    await nextTick()

    expect(fuel().options.ready!()).toBe(true)
    expect(fuel().load).toHaveBeenCalledOnce()
  })

  it('drops the speed camera layer once the server says it does not serve one', async () => {
    useMapSettingsStore().speedCamerasEnabled = true
    vi.mocked(mapApi.poi).mockResolvedValue({ items: [], hasMore: false, available: false })
    const { result } = setup('hev')
    expect(speedCameras().options.enabled.value).toBe(true)

    await speedCameras().options.fetchItems({ lat: 52, lng: 5 } as never, 10)

    expect(result.speedCamerasAvailable.value).toBe(false)
    expect(speedCameras().options.enabled.value).toBe(false)
  })

  it('sums the connectors of a kind in the charging popup, and escapes what OCM sends', () => {
    setup('bev')
    const marker = charging().options.toMarker(
      station([22, 22, 50], {
        title: '<b>Laadplein</b>',
        numberOfPoints: 3,
        town: 'Utrecht',
        addressLine: 'Stationsplein 1',
      }),
    ) as unknown as { popup: () => string }

    const html = marker.popup()
    expect(html).toContain('&lt;b&gt;Laadplein&lt;/b&gt;')
    expect(html).toContain('<li>Type 2 · 22 kW ×2</li>')
    expect(html).toContain('<li>Type 2 · 50 kW</li>')
    expect(html).toContain('3 stalls')
    expect(html).toContain('Stationsplein 1, Utrecht')
  })

  it("lists a fuel station's fuels in its popup", () => {
    setup('hev')
    const marker = fuel().options.toMarker(
      poi({ brand: 'Shell', 'fuel:diesel': 'yes', 'fuel:lpg': 'yes' }),
    ) as unknown as { popup: () => string }

    const html = marker.popup()
    expect(html).toContain('Shell')
    expect(html).toContain('Diesel · LPG')
  })

  it('loads again a moment after the map moves, and lets go of the map and the layers when done', async () => {
    const { wrapper, map } = setup('hev')
    await nextTick()

    map!.handlers.get('moveend zoomend')!()
    for (const layer of layers) expect(layer.scheduleLoad).toHaveBeenCalledWith(500)

    wrapper.unmount()
    expect(map!.off).toHaveBeenCalledWith('moveend zoomend', expect.any(Function))
    for (const layer of layers) expect(layer.dispose).toHaveBeenCalledOnce()
  })
})
