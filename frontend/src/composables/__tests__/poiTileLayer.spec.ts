import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, shallowRef, nextTick } from 'vue'
import type { LeafletMap } from '@/utils/leaflet'

// Leaflet is stubbed: this suite is about the fetch/cache/redraw bookkeeping, not about what
// the markers look like. The cluster records what was added so drawing can still be asserted.
const clusterLayers: unknown[][] = []

vi.mock('@/utils/leaflet', () => {
  const marker = (id: string) => ({ id })
  return {
    L: {
      divIcon: () => ({}),
      marker,
      markerClusterGroup: () => {
        const layers: unknown[] = []
        clusterLayers.push(layers)
        return {
          addTo: () => undefined,
          addLayer: (layer: unknown) => layers.push(layer),
          remove: () => undefined,
        }
      },
    },
  }
})
vi.mock('leaflet.markercluster', () => ({}))
vi.mock('leaflet.markercluster/dist/MarkerCluster.css', () => ({}))

const { createTileLayer } = await import('../poiTileLayer')

interface Poi {
  id: string
}

// A viewport of exactly one tile (half-degree cells), centred on 52.25/5.25.
function fakeMap(): LeafletMap {
  const center = { lat: 52.25, lng: 5.25, distanceTo: () => 5_000 }
  return {
    getBounds: () => ({
      getSouth: () => 52.1,
      getNorth: () => 52.4,
      getWest: () => 5.1,
      getEast: () => 5.4,
      getCenter: () => center,
      getNorthEast: () => ({ lat: 52.4, lng: 5.4 }),
    }),
  } as unknown as LeafletMap
}

function setup(
  responses: { items: Poi[]; hasMore: boolean }[],
  overrides: Partial<Parameters<typeof createTileLayer<Poi>>[0]> = {},
) {
  const fetchItems = vi.fn<() => Promise<{ items: Poi[]; hasMore: boolean }>>(
    async () => responses.shift() ?? { items: [], hasMore: false },
  )
  const enabled = ref(true)
  // shallowRef: a Leaflet map is not reactive data, and deep unwrapping mangles its type.
  const map = shallowRef<LeafletMap | null>(fakeMap())
  const loading: number[] = []
  const layer = createTileLayer<Poi>({
    map,
    enabled,
    clusterClassName: 'test-cluster',
    idOf: (item) => item.id,
    fetchItems,
    toMarker: (item) => ({ id: item.id }) as never,
    moreToFetch: ({ hasMore }) => hasMore,
    chainDelayMs: () => 1,
    onLoadingChange: (delta) => loading.push(delta),
    ...overrides,
  })
  return { layer, fetchItems, enabled, map, loading }
}

describe('createTileLayer', () => {
  beforeEach(() => {
    clusterLayers.length = 0
  })

  it('caches items and skips a viewport it has already covered', async () => {
    const { layer, fetchItems } = setup([{ items: [{ id: 'a' }, { id: 'b' }], hasMore: false }])

    await layer.load()
    await layer.load()

    expect(fetchItems).toHaveBeenCalledTimes(1)
    expect([...layer.items.keys()]).toEqual(['a', 'b'])
  })

  it('keeps items it has already seen when they come back again', async () => {
    const { layer } = setup([
      { items: [{ id: 'a' }], hasMore: false },
      { items: [{ id: 'a' }, { id: 'c' }], hasMore: false },
    ])

    await layer.load()
    // A radius override is an explicit refetch, so it bypasses the covered-tile check.
    await layer.load(100)

    expect([...layer.items.keys()]).toEqual(['a', 'c'])
  })

  it('draws only the items the filter includes', async () => {
    const { layer } = setup([{ items: [{ id: 'keep' }, { id: 'drop' }], hasMore: false }], {
      include: (item) => item.id === 'keep',
    })

    await layer.load()

    expect(clusterLayers[clusterLayers.length - 1]).toEqual([{ id: 'keep' }])
  })

  it('redraws from the cache without fetching again', async () => {
    const { layer, fetchItems } = setup([{ items: [{ id: 'a' }], hasMore: false }])
    await layer.load()

    layer.redraw()

    expect(fetchItems).toHaveBeenCalledTimes(1)
    expect(clusterLayers[clusterLayers.length - 1]).toEqual([{ id: 'a' }])
  })

  it('does not fetch while the layer is waiting for a precondition', async () => {
    let vehicleTypeKnown = false
    const { layer, fetchItems } = setup([{ items: [{ id: 'a' }], hasMore: false }], {
      ready: () => vehicleTypeKnown,
    })

    await layer.load()
    expect(fetchItems).not.toHaveBeenCalled()

    vehicleTypeKnown = true
    await layer.load()
    expect(fetchItems).toHaveBeenCalledTimes(1)
  })

  it('drops a response that arrives after the layer was switched off', async () => {
    const { layer, enabled } = setup([{ items: [{ id: 'a' }], hasMore: false }])

    const inFlight = layer.load()
    enabled.value = false
    await inFlight

    expect(layer.items.size).toBe(0)
  })

  it('clears everything when the layer is switched off', async () => {
    const { layer, enabled } = setup([{ items: [{ id: 'a' }], hasMore: false }])
    await layer.load()
    expect(layer.items.size).toBe(1)

    enabled.value = false
    await nextTick()

    expect(layer.items.size).toBe(0)
  })

  it('reloads when the layer is switched back on', async () => {
    const { layer, enabled, fetchItems } = setup([
      { items: [{ id: 'a' }], hasMore: false },
      { items: [{ id: 'a' }], hasMore: false },
    ])
    await layer.load()

    enabled.value = false
    await nextTick()
    enabled.value = true
    await nextTick()
    await Promise.resolve()

    expect(fetchItems).toHaveBeenCalledTimes(2)
    expect(layer.items.size).toBe(1)
  })

  it('reports loading for the duration of a fetch', async () => {
    const { layer, loading } = setup([{ items: [], hasMore: false }])

    await layer.load()

    expect(loading).toEqual([1, -1])
  })

  it('survives a failing source without losing what it had', async () => {
    const { layer } = setup([{ items: [{ id: 'a' }], hasMore: false }])
    await layer.load()

    const failing = setup([])
    failing.fetchItems.mockRejectedValueOnce(new Error('overpass is down'))
    await expect(failing.layer.load(100)).resolves.toBeUndefined()

    expect(layer.items.size).toBe(1)
    expect(failing.loading).toEqual([1, -1])
  })

  it('stops fetching once the map is gone', async () => {
    const { layer, map, fetchItems } = setup([{ items: [{ id: 'a' }], hasMore: false }])

    map.value = null
    await layer.load()

    expect(fetchItems).not.toHaveBeenCalled()
  })
})
