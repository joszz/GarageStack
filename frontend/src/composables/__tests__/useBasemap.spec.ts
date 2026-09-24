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

vi.mock('@/utils/maplibreLayer', () => ({
  // Assigned onto the recorded layer rather than spread into a copy, so what the composable
  // holds and what this suite inspects are the same object. Each layer keeps one GL map, the
  // way MapLibre does: the composable compares identity to spot a restyle that outlived it.
  maplibreGL: (options: Record<string, unknown>) => {
    const glMap = { setStyle }
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

const fakeMap = () => ({ id: 'map' }) as unknown as LeafletMap

async function mountBasemap(map: LeafletMap | null) {
  const { useBasemap } = await import('@/composables/useBasemap')
  const mapRef = shallowRef<LeafletMap | null>(map)
  const wrapper = mount(
    defineComponent({
      setup() {
        useBasemap(mapRef)
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
    // The tile server's own sources carry the credit it asks for, so overriding it here would
    // replace a correct attribution with a guess.
    expect(vector?.options.attributionControl).toBeUndefined()
    const style = vector?.options.style as { layers: { layout: Record<string, unknown> }[] }
    expect(style.layers[0]!.layout['text-field']).toEqual([
      'coalesce',
      ['get', 'name:en'],
      DARK_LABEL,
    ])
    expect(lastOfKind('raster')).toBeUndefined()
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
