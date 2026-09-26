import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { defineComponent, h, shallowRef, nextTick } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import type { LeafletMap } from '@/utils/leaflet'

// Leaflet and MapLibre are both stubbed: this suite is about which basemap ends up on the map
// and what happens to it when the theme, the map or the component changes - not about rendering.
interface FakeLayer {
  kind: 'raster' | 'vector'
  url?: string
  options: Record<string, unknown>
  addedTo: unknown
  removed: boolean
}

const layers: FakeLayer[] = []

function fakeLayer(kind: FakeLayer['kind'], options: Record<string, unknown>, url?: string) {
  const layer: FakeLayer & { addTo: (map: unknown) => unknown; remove: () => void } = {
    kind,
    url,
    options,
    addedTo: null,
    removed: false,
    addTo(map: unknown) {
      this.addedTo = map
      return this
    },
    remove() {
      this.removed = true
    },
  }
  layers.push(layer)
  return layer
}

vi.mock('@/utils/leaflet', () => ({
  L: {
    tileLayer: (url: string, options: Record<string, unknown>) => fakeLayer('raster', options, url),
  },
}))

const setStyle = vi.fn<(style: unknown) => void>()

// The images a GL map holds, and the resolver it asks about the ones its style lacks.
type ImageResolver = (id: string) => void
let images: Map<string, { width: number; height: number; data: Uint8Array }>
let imageResolver: ImageResolver | null

vi.mock('@/utils/maplibreLayer', () => ({
  // Assigned onto the recorded layer rather than spread into a copy, so what the composable
  // holds and what this suite inspects are the same object. Each layer keeps one GL map, the
  // way MapLibre does: the composable compares identity to spot a restyle that outlived it.
  maplibreGL: (options: Record<string, unknown>) => {
    const glMap = {
      setStyle,
      setMissingStyleImageResolver: (resolver: ImageResolver | null) => {
        imageResolver = resolver
      },
      hasImage: (id: string) => images.has(id),
      addImage: (id: string, image: { width: number; height: number; data: Uint8Array }) => {
        if (images.has(id)) throw new Error(`An image named "${id}" already exists.`)
        images.set(id, image)
      },
    }
    return Object.assign(fakeLayer('vector', options), { getMaplibreMap: () => glMap })
  },
}))

let webGl = true
vi.mock('@/utils/basemapStyle', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/basemapStyle')>()),
  supportsWebGl: () => webGl,
}))

const DARK_LABEL = ['coalesce', ['get', 'name_en'], ['get', 'name']]

function styleDoc(name: string) {
  return JSON.stringify({
    version: 8,
    name,
    layers: [{ id: 'place_label', type: 'symbol', layout: { 'text-field': DARK_LABEL } }],
  })
}

interface StyleResponse {
  ok: boolean
  status: number
  text: () => Promise<string>
}

const fetchMock = vi.fn<(url: string) => Promise<StyleResponse>>(async (url) => ({
  ok: true,
  status: 200,
  text: async () => styleDoc(url.endsWith('dark') ? 'dark' : 'light'),
}))

function lastOfKind(kind: FakeLayer['kind']): FakeLayer | undefined {
  return [...layers].reverse().find((l) => l.kind === kind)
}

// Only what the composable touches. setMaxZoom matters: Leaflet reads its zoom limits off tile
// layers, and the GL basemap is not one, so the composable has to state the limit itself.
interface FakeMap {
  id: string
  maxZoom: number | undefined
  container: HTMLElement
  setMaxZoom: (zoom: number) => void
  getContainer: () => HTMLElement
}

const fakeMap = () => {
  const map: FakeMap = {
    id: 'map',
    maxZoom: undefined,
    container: document.createElement('div'),
    setMaxZoom(zoom: number) {
      this.maxZoom = zoom
    },
    getContainer() {
      return this.container
    },
  }
  return map as unknown as LeafletMap
}

const containerOf = (map: LeafletMap) => (map as unknown as FakeMap).container

const maxZoomOf = (map: LeafletMap) => (map as unknown as FakeMap).maxZoom

async function mountBasemap(
  map: LeafletMap | null,
  options?: import('@/composables/useBasemap').BasemapOptions,
) {
  const { useBasemap } = await import('@/composables/useBasemap')
  const mapRef = shallowRef<LeafletMap | null>(map)
  const wrapper = mount(
    defineComponent({
      setup() {
        useBasemap(mapRef, options)
        return () => h('div')
      },
    }),
  )
  await flushPromises()
  return { mapRef, wrapper }
}

async function uiStore() {
  const { useUiSettingsStore } = await import('@/stores/settingsUi')
  return useUiSettingsStore()
}

describe('useBasemap', () => {
  beforeEach(async () => {
    vi.resetModules()
    setActivePinia(createPinia())
    localStorage.clear()
    layers.length = 0
    images = new Map()
    imageResolver = null
    setStyle.mockReset()
    fetchMock.mockClear()
    webGl = true
    vi.stubGlobal('fetch', fetchMock)
    ;(await uiStore()).theme = 'dark'
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('renders the themed vector basemap, with labels in the UI language', async () => {
    const map = fakeMap()
    await mountBasemap(map)

    expect(fetchMock).toHaveBeenCalledWith('https://tiles.openfreemap.org/styles/dark')
    const vector = lastOfKind('vector')
    expect(vector?.addedTo).toBe(map)
    // OpenFreeMap's styles declare no attribution on their sources, so the plugin would find
    // nothing to credit and the map would show none at all - the credit is stated here instead.
    const control = vector?.options.attributionControl as { customAttribution: string } | undefined
    expect(control?.customAttribution).toContain('openstreetmap.org/copyright')
    expect(control?.customAttribution).toContain('OpenMapTiles')
    expect(control?.customAttribution).toContain('OpenFreeMap')
    const style = vector?.options.style as { layers: { layout: Record<string, unknown> }[] }
    expect(style.layers[0]!.layout['text-field']).toEqual([
      'coalesce',
      ['get', 'name:en'],
      DARK_LABEL,
    ])
    expect(lastOfKind('raster')).toBeUndefined()
  })

  // Regression: the GL layer registers no zoom limit with Leaflet, so map.getMaxZoom() stayed
  // Infinity and leaflet.markercluster threw instead of clustering. Every POI layer (fuel,
  // charging, service areas) silently drew nothing, because the layer engine treats a failed
  // load as a best-effort miss.
  it('gives the map a finite max zoom so clustered layers can be added', async () => {
    const map = fakeMap()
    await mountBasemap(map)

    expect(maxZoomOf(map)).toBe(20)
  })

  it('stays on raster tiles without loading the renderer when asked to', async () => {
    const map = fakeMap()
    await mountBasemap(map, { vector: false })

    expect(lastOfKind('raster')?.addedTo).toBe(map)
    expect(lastOfKind('vector')).toBeUndefined()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('marks a raster map for the stylesheet, and unmarks it when it goes', async () => {
    const map = fakeMap()
    const { wrapper } = await mountBasemap(map, { vector: false })
    expect(containerOf(map).classList.contains('basemap--raster')).toBe(true)

    wrapper.unmount()

    expect(containerOf(map).classList.contains('basemap--raster')).toBe(false)
  })

  it('gives the map a finite max zoom on the raster fallback too', async () => {
    webGl = false
    const map = fakeMap()
    await mountBasemap(map)

    expect(maxZoomOf(map)).toBe(19)
  })

  it('recolours the map in place when the theme changes', async () => {
    await mountBasemap(fakeMap())
    const vectorLayers = layers.filter((l) => l.kind === 'vector').length

    const ui = await uiStore()
    ui.theme = 'light'
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith('https://tiles.openfreemap.org/styles/positron')
    expect(setStyle).toHaveBeenCalledTimes(1)
    expect(setStyle.mock.calls[0]![0]).toMatchObject({ name: 'light' })
    // Restyling rather than rebuilding is the point: the layer on the map is the original one.
    expect(layers.filter((l) => l.kind === 'vector')).toHaveLength(vectorLayers)
  })

  it('relabels the map when the UI language changes', async () => {
    await mountBasemap(fakeMap())

    const ui = await uiStore()
    ui.locale = 'nl'
    await flushPromises()

    const style = setStyle.mock.calls[0]![0] as { layers: { layout: Record<string, unknown> }[] }
    expect(style.layers[0]!.layout['text-field']).toEqual([
      'coalesce',
      ['get', 'name:nl'],
      DARK_LABEL,
    ])
  })

  // OpenFreeMap's dark style asks for a "wood-pattern" its sprite lacks, and MapLibre warned about
  // it in the console for every tile that did.
  it('stands in a transparent pixel for an image the style asks for but does not have', async () => {
    await mountBasemap(fakeMap())

    imageResolver!('wood-pattern')
    // A second tile asking for it before the first has drawn must not add it twice.
    imageResolver!('wood-pattern')

    const standIn = images.get('wood-pattern')
    expect(standIn).toMatchObject({ width: 1, height: 1 })
    expect([...standIn!.data]).toEqual([0, 0, 0, 0])
  })

  it('falls back to raster tiles when the browser has no WebGL', async () => {
    webGl = false
    const map = fakeMap()
    await mountBasemap(map)

    const raster = lastOfKind('raster')
    expect(raster?.url).toBe('https://tile.openstreetmap.org/{z}/{x}/{y}.png')
    expect(raster?.options.maxZoom).toBe(19)
    // Raster tiles carry no source metadata, so this layer has to state the credit itself.
    expect(raster?.options.attribution).toContain('openstreetmap.org/copyright')
    expect(raster?.addedTo).toBe(map)
    expect(fetchMock).not.toHaveBeenCalled()
    expect(lastOfKind('vector')).toBeUndefined()
  })

  it('falls back to raster tiles when the style cannot be fetched', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    fetchMock.mockResolvedValueOnce({ ok: false, status: 503 } as never)

    await mountBasemap(fakeMap())

    expect(lastOfKind('raster')?.url).toBe('https://tile.openstreetmap.org/{z}/{x}/{y}.png')
    expect(lastOfKind('vector')).toBeUndefined()
  })

  it('moves the basemap when the map instance is replaced, and drops it on unmount', async () => {
    const { mapRef, wrapper } = await mountBasemap(fakeMap())
    const first = lastOfKind('vector')!

    const second = fakeMap()
    mapRef.value = second
    await nextTick()
    await flushPromises()

    expect(first.removed).toBe(true)
    const replacement = lastOfKind('vector')!
    expect(replacement).not.toBe(first)
    expect(replacement.addedTo).toBe(second)

    wrapper.unmount()
    expect(replacement.removed).toBe(true)
  })
})
