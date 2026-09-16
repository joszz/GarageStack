/**
 * The engine behind the map's point-of-interest layers. Charging stations, fuel stations and
 * service areas all work the same way: fetch by viewport tile, remember which tiles have been
 * covered, cache the items, and redraw a clustered marker layer from that cache. Only where the
 * items come from and how they are drawn differs, which is what the options below describe.
 */
import { watch, type Ref } from 'vue'
import { L, type LeafletMap } from '@/utils/leaflet'
import 'leaflet.markercluster'
import 'leaflet.markercluster/dist/MarkerCluster.css'

type ClusterFactory = {
  markerClusterGroup: (options?: {
    iconCreateFunction?: (cluster: { getChildCount: () => number }) => L.DivIcon
    maxClusterRadius?: number
    animate?: boolean
  }) => L.FeatureGroup
}
const leafWithCluster = L as typeof L & ClusterFactory

const MARKER_SIZE: L.PointExpression = [28, 28]
const MARKER_ANCHOR: L.PointExpression = [14, 14]

function createMarkerCluster(clusterClassName: string): L.FeatureGroup {
  return leafWithCluster.markerClusterGroup({
    maxClusterRadius: 60,
    animate: true,
    iconCreateFunction: (cluster) => {
      const count = cluster.getChildCount()
      return L.divIcon({
        className: '',
        html: `<div class="${clusterClassName}">${count}</div>`,
        iconSize: [36, 36],
        iconAnchor: [18, 18],
      })
    },
  })
}

export function createMarker(
  latitude: number,
  longitude: number,
  markerClassName: string,
  glyph: string,
  popupHtml: string,
): L.Marker {
  const icon = L.divIcon({
    className: '',
    html: `<div class="${markerClassName}">${glyph}</div>`,
    iconSize: MARKER_SIZE,
    iconAnchor: MARKER_ANCHOR,
  })
  return L.marker([latitude, longitude], { icon }).bindPopup(popupHtml)
}

function createDebouncer() {
  let timer: ReturnType<typeof setTimeout> | null = null
  return {
    trigger(fn: () => void, delayMs: number) {
      if (timer !== null) clearTimeout(timer)
      timer = setTimeout(() => {
        timer = null
        fn()
      }, delayMs)
    },
    cancel() {
      if (timer !== null) clearTimeout(timer)
      timer = null
    },
  }
}

// OSM/Open Charge Map data is crowd-editable - never trust it to be free of markup, so every
// externally-sourced string interpolated into a Leaflet popup (which uses innerHTML) must be escaped.
export function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

// Tiles are half-degree cells; the server caches per cell, so the client tracks which cells it
// has already pulled instead of refetching on every pan.
function tileKey(lat: number, lng: number): string {
  return `${Math.floor(lat * 2)},${Math.floor(lng * 2)}`
}

function computeVisibleTileKeys(map: LeafletMap): string[] {
  const bounds = map.getBounds()
  const minCellLat = Math.floor(bounds.getSouth() * 2)
  const maxCellLat = Math.floor(bounds.getNorth() * 2)
  const minCellLng = Math.floor(bounds.getWest() * 2)
  const maxCellLng = Math.floor(bounds.getEast() * 2)
  const keys: string[] = []
  for (let lat = minCellLat; lat <= maxCellLat; lat++) {
    for (let lng = minCellLng; lng <= maxCellLng; lng++) {
      keys.push(`${lat},${lng}`)
    }
  }
  return keys
}

/** What a finished fetch tells the layer about whether the viewport is fully covered yet. */
interface FetchOutcome {
  /** The server says it has more to give within this radius. */
  hasMore: boolean
  /** This response contained at least one item the layer had not seen. */
  newItems: boolean
  /** Items held by the layer after this response. */
  cached: number
}

export interface TileLayerOptions<T> {
  map: Ref<LeafletMap | null>
  /** Layer is switched on: off means "no markers", not "keep what is drawn". */
  enabled: Ref<boolean>
  /** Preconditions other than the toggle, e.g. a vehicle type the server needs. */
  ready?: () => boolean
  clusterClassName: string
  idOf: (item: T) => string
  fetchItems: (
    center: { lat: number; lng: number },
    radiusKm: number,
  ) => Promise<{ items: T[]; hasMore: boolean }>
  toMarker: (item: T) => L.Marker
  /** Client-side filter, applied on redraw so filter changes never cost an API call. */
  include?: (item: T) => boolean
  /** Whether tiles are still missing from the viewport after this response. */
  moreToFetch: (outcome: FetchOutcome) => boolean
  chainDelayMs: (outcome: FetchOutcome) => number
  onLoadingChange: (delta: number) => void
}

/**
 * One on-demand, clustered marker layer: fetches by viewport tile, caches what it has seen,
 * and redraws from that cache. Charging stations, fuel stations and service areas differ only
 * in where their items come from and how they are drawn, which is what the options describe.
 */
export function createTileLayer<T>(options: TileLayerOptions<T>) {
  const { map, enabled, clusterClassName, idOf, fetchItems, toMarker, include } = options
  const items = new Map<string, T>()
  const loadedTiles = new Set<string>()
  const debouncer = createDebouncer()
  let cluster: L.FeatureGroup | null = null
  let fetchId = 0

  function clear() {
    cluster?.remove()
    cluster = null
    loadedTiles.clear()
    items.clear()
  }

  function redraw() {
    const instance = map.value
    if (!instance) return
    cluster?.remove()
    cluster = null
    if (!enabled.value || items.size === 0) return

    cluster = createMarkerCluster(clusterClassName)
    cluster.addTo(instance)
    for (const item of items.values()) {
      if (include && !include(item)) continue
      cluster.addLayer(toMarker(item))
    }
  }

  function boundsRadiusKm(instance: LeafletMap): number {
    const bounds = instance.getBounds()
    const distanceM = bounds.getCenter().distanceTo(bounds.getNorthEast())
    return Math.min(Math.ceil(distanceM / 1000), 200)
  }

  async function load(overrideRadiusKm?: number): Promise<void> {
    const instance = map.value
    if (!instance || !enabled.value) {
      clear()
      return
    }
    // Not a failure, just too early: whatever the layer is waiting for reloads it when it lands.
    if (options.ready && !options.ready()) return

    const center = instance.getBounds().getCenter()
    // A radius override is an explicit "fetch this area now", so it skips the tile bookkeeping.
    const visibleKeys = overrideRadiusKm ? null : computeVisibleTileKeys(instance)
    if (visibleKeys && visibleKeys.every((key) => loadedTiles.has(key))) return

    const requestId = ++fetchId
    const radiusKm = overrideRadiusKm ?? boundsRadiusKm(instance)
    options.onLoadingChange(1)
    try {
      const response = await fetchItems({ lat: center.lat, lng: center.lng }, radiusKm)
      // A newer request or a toggle-off happened while this was in flight.
      if (requestId !== fetchId || !enabled.value) return

      let newItems = false
      for (const item of response.items) {
        const id = idOf(item)
        if (items.has(id)) continue
        items.set(id, item)
        newItems = true
      }

      loadedTiles.add(tileKey(center.lat, center.lng))
      if (newItems) redraw()
      if (!visibleKeys) return

      const outcome: FetchOutcome = { hasMore: response.hasMore, newItems, cached: items.size }
      if (!options.moreToFetch(outcome)) {
        // Nothing left for this viewport: mark it done so panning back costs no requests.
        for (const key of visibleKeys) loadedTiles.add(key)
      } else if (enabled.value && visibleKeys.some((key) => !loadedTiles.has(key))) {
        debouncer.trigger(() => load(), options.chainDelayMs(outcome))
      }
    } catch {
      // Every POI source is best-effort: rate limits, outages and offline are all non-fatal.
    } finally {
      options.onLoadingChange(-1)
    }
  }

  /** Debounced load, for bursts of map movement. A layer that is off ignores it. */
  function scheduleLoad(delayMs: number) {
    if (!enabled.value) return
    debouncer.trigger(() => load(), delayMs)
  }

  function dispose() {
    debouncer.cancel()
    clear()
  }

  // One watch per layer covers every reason it turns on or off: the settings toggle, and the
  // vehicle type making it irrelevant (no charging stations for an HEV, no fuel for a BEV).
  watch(enabled, (on) => {
    if (on) load()
    else clear()
  })

  return { items, load, redraw, clear, scheduleLoad, dispose }
}
