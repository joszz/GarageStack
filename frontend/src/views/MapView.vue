<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref, shallowRef, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import type { VehicleType } from '@/stores/vehicle'
import { useSettingsStore } from '@/stores/settings'
import { LMap, LTileLayer, LMarker, LPopup } from '@vue-leaflet/vue-leaflet'
import FiltersPanel from '@/components/FiltersPanel.vue'
import Slider from '@vueform/slider'
import Multiselect from '@vueform/multiselect'
import * as LModule from 'leaflet'
import type { Map as LeafletMap } from 'leaflet'
import 'leaflet.heat'
import 'leaflet.markercluster'
import 'leaflet.markercluster/dist/MarkerCluster.css'
import '@/assets/map.css'
import type { Trip, ChargingStation, PoiItem } from '@/services/api'
import { mapApi } from '@/services/api'

// Vite wraps CJS modules in a frozen ESM namespace - `import * as LModule` gives that frozen
// namespace. leaflet.heat patches the actual mutable CJS export (LModule.default), so we must
// use that reference to reach heatLayer at runtime.
const L = ((LModule as unknown as { default?: typeof LModule }).default ??
  LModule) as typeof LModule

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const store = useVehicleStore()
const settingsStore = useSettingsStore()

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)
const vehicleType = computed((): VehicleType | 'unknown' => {
  const override = settingsStore.vehicleTypeOverride
  if (override !== 'auto') return override as VehicleType
  return store.detectedVehicleType
})
const isHev = computed(() => vehicleType.value === 'hev')
const isBev = computed(() => vehicleType.value === 'bev')
const displayLocale = computed(() => (settingsStore.locale === 'nl' ? 'nl-NL' : 'en-US'))
const selectedTripIndex = ref<number | null>(null)
const heatmapEnabled = computed({
  get: () => settingsStore.heatmapEnabled,
  set: (v: boolean) => {
    settingsStore.heatmapEnabled = v
  },
})
const speedOverlayEnabled = computed({
  get: () => settingsStore.speedOverlayEnabled,
  set: (v: boolean) => {
    settingsStore.speedOverlayEnabled = v
  },
})
const routeOutlineEnabled = computed({
  get: () => settingsStore.routeOutlineEnabled,
  set: (v: boolean) => {
    settingsStore.routeOutlineEnabled = v
  },
})
const chargingStationsEnabled = computed({
  get: () => settingsStore.chargingStationsEnabled,
  set: (v: boolean) => {
    settingsStore.chargingStationsEnabled = v
  },
})
const fuelStationsEnabled = computed({
  get: () => settingsStore.fuelStationsEnabled,
  set: (v: boolean) => {
    settingsStore.fuelStationsEnabled = v
  },
})
const serviceAreasEnabled = computed({
  get: () => settingsStore.serviceAreasEnabled,
  set: (v: boolean) => {
    settingsStore.serviceAreasEnabled = v
  },
})
const fuelBrandFilter = computed({
  get: () => settingsStore.fuelBrandFilter,
  set: (v: string[]) => {
    settingsStore.fuelBrandFilter = v
  },
})
const cachedFuelBrands = ref<string[]>([])
const brandsLoading = ref(false)

const availableFuelBrands = computed(() => {
  const brands = new Set<string>(cachedFuelBrands.value)
  for (const item of fuelAllItems.values()) {
    const tags = item.tags ?? {}
    const brand = tags['brand'] ?? tags['operator'] ?? null
    if (brand) brands.add(brand)
  }
  return [...brands].sort((a, b) => a.localeCompare(b))
})

async function loadFuelBrands() {
  if (vehicleType.value === 'unknown') return
  brandsLoading.value = true
  try {
    cachedFuelBrands.value = await mapApi.poiBrands('fuel', vehicleType.value)
  } catch {
    // non-fatal
  } finally {
    brandsLoading.value = false
  }
}
const chargingMinPowerKw = computed({
  get: () => settingsStore.chargingMinPowerKw,
  set: (v: number) => {
    settingsStore.chargingMinPowerKw = v
  },
})
const chargingMaxPowerKw = computed({
  get: () => settingsStore.chargingMaxPowerKw,
  set: (v: number) => {
    settingsStore.chargingMaxPowerKw = v
  },
})

// Slider value: [minKw, maxKw] where max=350 means "no upper limit" (stored as 0 in settings)
const powerRangeSlider = computed({
  get: (): [number, number] => [
    chargingMinPowerKw.value,
    chargingMaxPowerKw.value === 0 ? 350 : chargingMaxPowerKw.value,
  ],
  set: (value: number[]) => {
    chargingMinPowerKw.value = value[0]!
    chargingMaxPowerKw.value = (value[1] ?? 350) >= 350 ? 0 : value[1]!
  },
})

const powerRangeLabel = computed(() => {
  const min = chargingMinPowerKw.value
  const max = chargingMaxPowerKw.value
  if (min === 0 && max === 0) return t('trips.chargingPowerAny')
  const minStr = min === 0 ? t('trips.chargingPowerAny') : `${min} kW`
  const maxStr = max === 0 ? '350+ kW' : `${max} kW`
  return `${minStr} - ${maxStr}`
})

function formatPowerTooltip(value: number): string {
  if (value === 0) return t('trips.chargingPowerAny')
  if (value >= 350) return '350+'
  return String(value)
}
let shouldSelectLatest = route.query.selectLatest === '1'

const dateRangeDays = computed({
  get: () => settingsStore.filterDays,
  set: (v: number) => {
    settingsStore.filterDays = v
  },
})
const LOAD_MORE_SIZE = 10
const displayCount = ref(LOAD_MORE_SIZE)
const sentinelRef = ref<HTMLElement | null>(null)

const mapWrapperRef = ref<HTMLElement | null>(null)
const tripSidebarRef = ref<HTMLElement | null>(null)
const mapInstance = shallowRef<LeafletMap | null>(null)
let heatLayer: L.Layer | null = null
let routeLines: L.Polyline[] = []
let startMarker: L.Marker | null = null
let endMarker: L.Marker | null = null
let chargingCluster: L.FeatureGroup | null = null
let chargingDebounceTimer: ReturnType<typeof setTimeout> | null = null
let chargingFetchId = 0
let fuelCluster: L.FeatureGroup | null = null
let serviceAreaCluster: L.FeatureGroup | null = null
let fuelDebounceTimer: ReturnType<typeof setTimeout> | null = null
let serviceAreaDebounceTimer: ReturnType<typeof setTimeout> | null = null
let fuelFetchId = 0
let serviceAreaFetchId = 0
const fuelLoadedTiles = new Set<string>()
const fuelLoadedIds = new Set<string>()
// All fuel stations fetched so far, keyed by externalId. The brand filter is applied
// client-side from this cache so checkbox changes never trigger a new API call.
const fuelAllItems = new Map<string, PoiItem>()
const serviceAreaLoadedTiles = new Set<string>()
const serviceAreaLoadedIds = new Set<string>()
const chargingLoadedTiles = new Set<string>()
// All stations fetched so far, keyed by station ID. The power filter is applied
// client-side from this cache so slider changes never trigger a new API call.
const chargingAllStations = new Map<string, ChargingStation>()
const poiLoadingCount = ref(0)
const poiLoading = computed(() => poiLoadingCount.value > 0)
let resizeObserver: ResizeObserver | null = null
let infiniteScrollObserver: IntersectionObserver | null = null
let mapUpdateRaf: number | null = null
let hasCenteredOnStatus = false

type HeatLayerFactory = {
  heatLayer: (
    latlngs: Array<[number, number] | [number, number, number]>,
    options?: {
      minOpacity?: number
      maxZoom?: number
      max?: number
      radius?: number
      blur?: number
      gradient?: Record<number, string>
    },
  ) => L.Layer
}

const leafWithHeat = L as typeof L & HeatLayerFactory

type ClusterFactory = {
  markerClusterGroup: (options?: {
    iconCreateFunction?: (cluster: { getChildCount: () => number }) => L.DivIcon
    maxClusterRadius?: number
    animate?: boolean
  }) => L.FeatureGroup
}
const leafWithCluster = L as typeof L & ClusterFactory

const tripColors = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899']

function tripColor(index: number): string {
  return tripColors[index % tripColors.length] ?? '#3b82f6'
}

function tripColorClass(index: number): string {
  return `trip-list__dot--${index % tripColors.length}`
}

const speedStops: Array<{ speed: number; r: number; g: number; b: number }> = [
  { speed: 0, r: 16, g: 185, b: 129 },
  { speed: 50, r: 132, g: 204, b: 22 },
  { speed: 90, r: 245, g: 158, b: 11 },
  { speed: 120, r: 249, g: 115, b: 22 },
  { speed: 150, r: 239, g: 68, b: 68 },
]

function speedToColor(speed: number | null, fallback: string): string {
  if (speed === null) return fallback
  const s = Math.max(0, speed)
  const last = speedStops[speedStops.length - 1]!
  if (s >= last.speed) return `rgb(${last.r},${last.g},${last.b})`
  let lo = speedStops[0]!
  let hi = last
  for (let i = 0; i < speedStops.length - 1; i++) {
    if (s >= speedStops[i]!.speed && s < speedStops[i + 1]!.speed) {
      lo = speedStops[i]!
      hi = speedStops[i + 1]!
      break
    }
  }
  const t = (s - lo.speed) / (hi.speed - lo.speed)
  const r = Math.round(lo.r + t * (hi.r - lo.r))
  const g = Math.round(lo.g + t * (hi.g - lo.g))
  const b = Math.round(lo.b + t * (hi.b - lo.b))
  return `rgb(${r},${g},${b})`
}

// Static initial centre - controlled by fitAll/flyToStatus after data loads.
const center: [number, number] = [52.3676, 4.9041]

// Trips displayed newest-first in the sidebar; selectedTripIndex is always the real store.trips index.
const newestFirstTrips = computed(() => [...store.trips].reverse())
const displayTrips = computed(() => newestFirstTrips.value.slice(0, displayCount.value))

function realIndex(newestFirstIdx: number): number {
  return store.trips.length - 1 - newestFirstIdx
}

const allPoints = computed<[number, number][]>(() =>
  store.trips.flatMap((trip) =>
    trip.points.map((p) => [p.latitude, p.longitude] as [number, number]),
  ),
)

function formatDuration(startedAt: string, endedAt: string): string {
  const ms = new Date(endedAt).getTime() - new Date(startedAt).getTime()
  const mins = Math.round(ms / 60_000)
  if (mins < 60) return `${mins} min`
  return `${Math.floor(mins / 60)}h ${mins % 60}m`
}

function clearRouteLines() {
  routeLines.forEach((l) => l.remove())
  routeLines = []
  if (startMarker) {
    startMarker.remove()
    startMarker = null
  }
  if (endMarker) {
    endMarker.remove()
    endMarker = null
  }
}

function buildHeatLayer() {
  const map = mapInstance.value
  if (!map || allPoints.value.length === 0 || !heatmapEnabled.value) return
  if (typeof leafWithHeat.heatLayer !== 'function') {
    console.warn('[map] leaflet.heat plugin not available')
    return
  }
  if (heatLayer) {
    heatLayer.remove()
    heatLayer = null
  }
  heatLayer = leafWithHeat
    .heatLayer(allPoints.value, {
      radius: 18,
      blur: 22,
      maxZoom: 17,
      gradient: { 0.4: '#3b82f6', 0.65: '#f59e0b', 1.0: '#ef4444' },
    })
    .addTo(map)
}

function buildRouteLines() {
  const map = mapInstance.value
  if (!map) return
  clearRouteLines()
  store.trips.forEach((trip, i) => {
    const pts = trip.points.map((p) => [p.latitude, p.longitude] as [number, number])
    if (pts.length < 2) return
    if (routeOutlineEnabled.value) {
      const border = L.polyline(pts, { color: '#111', weight: 7, opacity: 0.4 })
      border.on('click', () => selectTrip(i))
      border.addTo(map)
      routeLines.push(border)
    }
    const line = L.polyline(pts, { color: tripColor(i), weight: 3, opacity: 0.75 })
    line.on('click', () => selectTrip(i))
    line.addTo(map)
    routeLines.push(line)
  })
}

function buildSelectedLine() {
  const map = mapInstance.value
  const idx = selectedTripIndex.value
  if (!map || idx === null) return
  clearRouteLines()
  const trip = store.trips[idx]
  if (!trip) return
  const pts = trip.points
  if (pts.length < 2) return

  const coords = pts.map((p) => [p.latitude, p.longitude] as [number, number])

  if (routeOutlineEnabled.value) {
    const border = L.polyline(coords, { color: '#111', weight: 9, opacity: 0.4 })
    border.addTo(map)
    routeLines.push(border)
  }

  if (speedOverlayEnabled.value) {
    for (let i = 0; i < pts.length - 1; i++) {
      const p0 = pts[i]!
      const p1 = pts[i + 1]!
      const color = speedToColor(p0.speed, tripColor(idx))
      const segment = L.polyline(
        [
          [p0.latitude, p0.longitude],
          [p1.latitude, p1.longitude],
        ],
        { color, weight: 5, opacity: 1, lineCap: 'square' },
      )
      segment.addTo(map)
      routeLines.push(segment)
    }
  } else {
    const line = L.polyline(coords, { color: tripColor(idx), weight: 5, opacity: 1 })
    line.addTo(map)
    routeLines.push(line)
  }

  buildTripMarkers(trip)
}

function buildTripMarkers(trip: Trip) {
  const map = mapInstance.value
  if (!map || trip.points.length === 0) return

  const start = trip.points[0]
  const end = trip.points[trip.points.length - 1]
  if (!start || !end) return

  const startIcon = L.divIcon({
    className: '',
    html: '<div class="trip-marker trip-marker--start"></div>',
    iconSize: [16, 16],
    iconAnchor: [8, 8],
  })
  startMarker = L.marker([start.latitude, start.longitude], { icon: startIcon }).addTo(map)

  const endIcon = L.divIcon({
    className: '',
    html: '<div class="trip-marker trip-marker--end"><div class="trip-flag-pole"></div><div class="trip-flag-flag"></div></div>',
    iconSize: [20, 32],
    iconAnchor: [2, 32],
  })
  endMarker = L.marker([end.latitude, end.longitude], { icon: endIcon }).addTo(map)
}

function removeHeatLayer() {
  if (heatLayer) {
    heatLayer.remove()
    heatLayer = null
  }
}

function clearChargingMarkers() {
  chargingCluster?.remove()
  chargingCluster = null
  chargingLoadedTiles.clear()
  chargingAllStations.clear()
}

function redrawChargingMarkers() {
  const map = mapInstance.value
  if (!map) return
  chargingCluster?.remove()
  chargingCluster = null
  if (!chargingStationsEnabled.value || chargingAllStations.size === 0) return

  chargingCluster = leafWithCluster.markerClusterGroup({
    maxClusterRadius: 60,
    animate: true,
    iconCreateFunction: (cluster) => {
      const count = cluster.getChildCount()
      return L.divIcon({
        className: '',
        html: `<div class="charging-cluster">${count}</div>`,
        iconSize: [36, 36],
        iconAnchor: [18, 18],
      })
    },
  })
  chargingCluster.addTo(map)

  const minKw = chargingMinPowerKw.value
  const maxKw = chargingMaxPowerKw.value

  for (const station of chargingAllStations.values()) {
    if (minKw > 0 && !station.connectors.some((c) => c.powerKw != null && c.powerKw >= minKw))
      continue
    if (maxKw > 0 && !station.connectors.some((c) => c.powerKw == null || c.powerKw <= maxKw))
      continue
    const cls = station.isOperational === false ? ' charging-marker--unknown' : ''
    const icon = L.divIcon({
      className: '',
      html: `<div class="charging-marker${cls}">&#9889;</div>`,
      iconSize: [28, 28],
      iconAnchor: [14, 14],
    })
    const marker = L.marker([station.latitude, station.longitude], { icon })
    marker.bindPopup(buildChargingPopup(station))
    chargingCluster.addLayer(marker)
  }
}

function clearPoiMarkers(poiType: 'fuel' | 'service_area') {
  if (poiType === 'fuel') {
    fuelCluster?.remove()
    fuelCluster = null
    fuelLoadedTiles.clear()
    fuelLoadedIds.clear()
    fuelAllItems.clear()
  } else {
    serviceAreaCluster?.remove()
    serviceAreaCluster = null
    serviceAreaLoadedTiles.clear()
    serviceAreaLoadedIds.clear()
  }
}

function redrawFuelMarkers() {
  const map = mapInstance.value
  if (!map) return
  fuelCluster?.remove()
  fuelCluster = null
  if (!fuelStationsEnabled.value || fuelAllItems.size === 0) return

  fuelCluster = leafWithCluster.markerClusterGroup({
    maxClusterRadius: 60,
    animate: true,
    iconCreateFunction: (c) => {
      const count = c.getChildCount()
      return L.divIcon({
        className: '',
        html: `<div class="poi-cluster poi-cluster--fuel">${count}</div>`,
        iconSize: [36, 36],
        iconAnchor: [18, 18],
      })
    },
  })
  fuelCluster.addTo(map)

  const selectedBrands = fuelBrandFilter.value
  for (const item of fuelAllItems.values()) {
    const tags = item.tags ?? {}
    const brand = tags['brand'] ?? tags['operator'] ?? null
    if (selectedBrands.length > 0 && (brand === null || !selectedBrands.includes(brand))) continue
    const icon = L.divIcon({
      className: '',
      html: '<div class="poi-marker poi-marker--fuel">&#9981;</div>',
      iconSize: [28, 28],
      iconAnchor: [14, 14],
    })
    const marker = L.marker([item.latitude, item.longitude], { icon })
    marker.bindPopup(buildPoiPopup(item))
    fuelCluster.addLayer(marker)
  }
}

function buildPoiPopup(item: PoiItem): string {
  const tags = item.tags ?? {}
  const brand = tags['brand'] ?? tags['operator'] ?? null
  const openingHours = tags['opening_hours'] ?? null
  return `<div class="poi-popup">
    <strong class="poi-popup__title">${item.name ?? item.poiType}</strong>
    ${brand ? `<div class="poi-popup__meta">${brand}</div>` : ''}
    ${openingHours ? `<div class="poi-popup__meta">${openingHours}</div>` : ''}
  </div>`
}

async function loadPoiLayer(poiType: 'fuel' | 'service_area', overrideRadius?: number) {
  const map = mapInstance.value
  const enabled = poiType === 'fuel' ? fuelStationsEnabled.value : serviceAreasEnabled.value
  if (!map || !enabled) {
    clearPoiMarkers(poiType)
    return
  }

  // Vehicle type not resolved yet - wait for the vehicleType watch to retry once config loads
  if (poiType === 'fuel' && vehicleType.value === 'unknown') return

  const loadedTiles = poiType === 'fuel' ? fuelLoadedTiles : serviceAreaLoadedTiles
  const loadedIds = poiType === 'fuel' ? fuelLoadedIds : serviceAreaLoadedIds
  const vt = vehicleType.value
  const bounds = map.getBounds()
  const center = bounds.getCenter()
  const centerKey = `${Math.floor(center.lat * 2)},${Math.floor(center.lng * 2)}`

  // Skip only when every visible tile has already been fetched.
  const visibleKeys = overrideRadius ? null : computeVisibleTileKeys(map)
  if (visibleKeys && visibleKeys.every((k) => loadedTiles.has(k))) return

  const radiusKm = overrideRadius ?? getBoundsRadiusKm()
  const fetchId = poiType === 'fuel' ? ++fuelFetchId : ++serviceAreaFetchId

  poiLoadingCount.value++
  try {
    const { items, hasMore } = await mapApi.poi(poiType, center.lat, center.lng, radiusKm, vt)
    const currentId = poiType === 'fuel' ? fuelFetchId : serviceAreaFetchId
    if (fetchId !== currentId || !enabled) return

    let newItems = false

    if (poiType === 'fuel') {
      for (const item of items) {
        if (fuelAllItems.has(item.externalId)) continue
        fuelAllItems.set(item.externalId, item)
        loadedIds.add(item.externalId)
        newItems = true
      }
      if (newItems) redrawFuelMarkers()
    } else {
      let cluster = serviceAreaCluster
      if (!cluster) {
        cluster = leafWithCluster.markerClusterGroup({
          maxClusterRadius: 60,
          animate: true,
          iconCreateFunction: (c) => {
            const count = c.getChildCount()
            return L.divIcon({
              className: '',
              html: `<div class="poi-cluster poi-cluster--service-area">${count}</div>`,
              iconSize: [36, 36],
              iconAnchor: [18, 18],
            })
          },
        })
        cluster.addTo(map)
        serviceAreaCluster = cluster
      }
      for (const item of items) {
        if (loadedIds.has(item.externalId)) continue
        loadedIds.add(item.externalId)
        newItems = true
        const icon = L.divIcon({
          className: '',
          html: '<div class="poi-marker poi-marker--service-area">&#9654;</div>',
          iconSize: [28, 28],
          iconAnchor: [14, 14],
        })
        const marker = L.marker([item.latitude, item.longitude], { icon })
        marker.bindPopup(buildPoiPopup(item))
        cluster.addLayer(marker)
      }
    }

    loadedTiles.add(centerKey)

    // hasMore = server still has uncached tiles in this radius (either beyond MaxOnDemandTiles,
    // or a fetch was skipped due to Overpass rate-limit backoff).
    // When !hasMore: all tiles are cached -- bulk-mark the viewport and stop.
    // When hasMore: chain another request. Use a longer delay when no new items arrived
    // (server is likely in a rate-limit backoff window) to avoid hammering the API.
    if (visibleKeys) {
      if (!hasMore) {
        for (const k of visibleKeys) loadedTiles.add(k)
      } else if (enabled && visibleKeys.some((k) => !loadedTiles.has(k))) {
        const chainDelay = newItems ? 400 : 5000
        if (poiType === 'fuel') {
          if (fuelDebounceTimer !== null) clearTimeout(fuelDebounceTimer)
          fuelDebounceTimer = setTimeout(() => {
            fuelDebounceTimer = null
            loadPoiLayer(poiType)
          }, chainDelay)
        } else {
          if (serviceAreaDebounceTimer !== null) clearTimeout(serviceAreaDebounceTimer)
          serviceAreaDebounceTimer = setTimeout(() => {
            serviceAreaDebounceTimer = null
            loadPoiLayer(poiType)
          }, chainDelay)
        }
      }
    }
  } catch {
    // Overpass errors are non-fatal
  } finally {
    poiLoadingCount.value--
  }
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

function getBoundsRadiusKm(): number {
  const map = mapInstance.value
  if (!map) return 10
  const bounds = map.getBounds()
  const distanceM = bounds.getCenter().distanceTo(bounds.getNorthEast())
  return Math.min(Math.ceil(distanceM / 1000), 200)
}

function buildChargingPopup(station: ChargingStation): string {
  // Group connectors by type+power, summing quantity so "11 kW × 4" shows instead
  // of four identical rows when OCM returns one record per port rather than one with Quantity=4.
  const grouped = new Map<string, { type: string | null; powerKw: number | null; count: number }>()
  for (const c of station.connectors) {
    if (!c.type && c.powerKw == null) continue
    const key = `${c.type ?? ''}|${c.powerKw ?? ''}`
    const existing = grouped.get(key)
    const qty = c.quantity ?? 1
    if (existing) {
      existing.count += qty
    } else {
      grouped.set(key, { type: c.type, powerKw: c.powerKw, count: qty })
    }
  }

  const connectorItems = [...grouped.values()]
    .map(({ type, powerKw, count }) => {
      const parts = [type, powerKw != null ? `${powerKw} kW` : null].filter(Boolean)
      const suffix = count > 1 ? ` ×${count}` : ''
      return `<li>${parts.join(' · ')}${suffix}</li>`
    })
    .join('')

  const stallLine =
    station.numberOfPoints != null
      ? `<div class="charging-popup__stalls">${station.numberOfPoints} ${station.numberOfPoints === 1 ? t('trips.chargingStall') : t('trips.chargingStalls')}</div>`
      : ''

  return `<div class="charging-popup">
    <strong class="charging-popup__title">${station.title}</strong>
    ${station.operator ? `<div class="charging-popup__operator">${station.operator}</div>` : ''}
    ${station.addressLine || station.town ? `<div class="charging-popup__address">${[station.addressLine, station.town].filter(Boolean).join(', ')}</div>` : ''}
    ${stallLine}
    ${connectorItems ? `<ul class="charging-popup__connectors">${connectorItems}</ul>` : ''}
  </div>`
}

async function loadChargingStations(overrideRadius?: number) {
  const map = mapInstance.value
  if (!map || !chargingStationsEnabled.value || isHev.value) {
    clearChargingMarkers()
    return
  }

  const mc = map.getBounds().getCenter()
  const center = { lat: mc.lat, lng: mc.lng }
  const centerKey = `${Math.floor(center.lat * 2)},${Math.floor(center.lng * 2)}`

  // Skip only when every visible tile has already been fetched.
  const visibleKeys = overrideRadius ? null : computeVisibleTileKeys(map)
  if (visibleKeys && visibleKeys.every((k) => chargingLoadedTiles.has(k))) return

  const fetchId = ++chargingFetchId
  const radiusKm = overrideRadius ?? getBoundsRadiusKm()
  poiLoadingCount.value++
  try {
    // Always fetch unfiltered (0, 0) -- the power filter is applied client-side from
    // chargingAllStations so slider changes never need a new API call.
    const stations = await mapApi.chargingStations(center.lat, center.lng, radiusKm, 0, 0)
    if (fetchId !== chargingFetchId || !chargingStationsEnabled.value) return

    let newStations = false
    for (const station of stations) {
      const id = String(station.id)
      if (!chargingAllStations.has(id)) {
        chargingAllStations.set(id, station)
        newStations = true
      }
    }

    chargingLoadedTiles.add(centerKey)
    if (newStations) redrawChargingMarkers()

    // When no new stations arrived the server has nothing more to cache for this viewport --
    // mark all visible tiles done. When new stations did arrive, chain another pass.
    if (visibleKeys) {
      if (!newStations && chargingAllStations.size > 0) {
        for (const k of visibleKeys) chargingLoadedTiles.add(k)
      } else if (
        chargingStationsEnabled.value &&
        visibleKeys.some((k) => !chargingLoadedTiles.has(k))
      ) {
        if (chargingDebounceTimer !== null) clearTimeout(chargingDebounceTimer)
        chargingDebounceTimer = setTimeout(() => {
          chargingDebounceTimer = null
          loadChargingStations()
        }, 400)
      }
    }
  } catch {
    // OCM is optional; silently ignore errors
  } finally {
    poiLoadingCount.value--
  }
}

function fitBoundsSafe(pts: [number, number][]) {
  const map = mapInstance.value
  if (!map || pts.length === 0) return
  const bounds = L.latLngBounds(pts)
  if (bounds.getNorthEast().equals(bounds.getSouthWest())) {
    map.setView(bounds.getCenter(), 15, { animate: false })
  } else {
    map.fitBounds(bounds, { padding: [32, 32], animate: false })
  }
}

function fitAll() {
  if (allPoints.value.length > 0) {
    fitBoundsSafe(allPoints.value)
  } else if (status.value?.latitude != null && status.value?.longitude != null) {
    mapInstance.value?.setView([status.value.latitude, status.value.longitude], 14, {
      animate: false,
    })
  }
}

function fitTrip(trip: Trip) {
  fitBoundsSafe(trip.points.map((p) => [p.latitude, p.longitude] as [number, number]))
}

function flyToStatus() {
  const map = mapInstance.value
  const s = status.value
  if (map && s?.latitude != null && s?.longitude != null) {
    selectedTripIndex.value = null
    map.setView([s.latitude, s.longitude], 14, { animate: false })
  }
}

function onMapReady(map: LeafletMap) {
  mapInstance.value = map

  // Keep map in sync when the flex container resizes (orientation change, sidebar toggle, etc.)
  if (mapWrapperRef.value) {
    resizeObserver = new ResizeObserver(() => map.invalidateSize())
    resizeObserver.observe(mapWrapperRef.value)
  }

  map.on('moveend zoomend', () => {
    if (chargingStationsEnabled.value) {
      if (chargingDebounceTimer !== null) clearTimeout(chargingDebounceTimer)
      chargingDebounceTimer = setTimeout(() => {
        chargingDebounceTimer = null
        loadChargingStations()
      }, 500)
    }
    if (fuelStationsEnabled.value) {
      if (fuelDebounceTimer !== null) clearTimeout(fuelDebounceTimer)
      fuelDebounceTimer = setTimeout(() => {
        fuelDebounceTimer = null
        loadPoiLayer('fuel')
      }, 500)
    }
    if (serviceAreasEnabled.value) {
      if (serviceAreaDebounceTimer !== null) clearTimeout(serviceAreaDebounceTimer)
      serviceAreaDebounceTimer = setTimeout(() => {
        serviceAreaDebounceTimer = null
        loadPoiLayer('service_area')
      }, 500)
    }
  })

  // nextTick: wait for Vue DOM → requestAnimationFrame: wait for browser layout pass.
  // Without rAF, clientHeight is still 0 on mobile because the flex heights haven't been
  // computed by the browser yet even though the DOM is ready.
  nextTick(() => {
    requestAnimationFrame(() => {
      map.invalidateSize()
      if (allPoints.value.length > 0) {
        buildHeatLayer()
        buildRouteLines()
        fitAll()
      } else if (status.value?.latitude != null && status.value?.longitude != null) {
        hasCenteredOnStatus = true
        map.setView([status.value.latitude, status.value.longitude], 14, { animate: false })
      }
      if (chargingStationsEnabled.value) loadChargingStations(100)
      if (fuelStationsEnabled.value) loadPoiLayer('fuel', 100)
      if (serviceAreasEnabled.value) loadPoiLayer('service_area', 100)
    })
  })
}

// Center on car position only once on initial load. Subsequent status updates (SignalR
// reconnects every ~60s) must not move the map away from where the user is looking.
watch(status, (s) => {
  if (!mapInstance.value || s?.latitude == null || s?.longitude == null) return
  if (!hasCenteredOnStatus && allPoints.value.length === 0) {
    hasCenteredOnStatus = true
    mapInstance.value.setView([s.latitude, s.longitude], 14, { animate: false })
  }
})

// When trips load after the map is ready, rebuild layers and fit
watch(allPoints, async (pts) => {
  if (pts.length === 0 || !mapInstance.value) return
  await nextTick()
  buildHeatLayer()
  buildRouteLines()
  if (shouldSelectLatest) {
    shouldSelectLatest = false
    selectTrip(store.trips.length - 1)
  } else {
    fitAll()
  }
})

// Trip selection drives map display and popover position.
// Leaflet operations are deferred to the next animation frame so the Vue DOM
// update (active state CSS transition, popover enter) renders cleanly before
// the canvas GPU layer is torn down and rebuilt.
watch(selectedTripIndex, (idx) => {
  if (mapUpdateRaf !== null) cancelAnimationFrame(mapUpdateRaf)
  mapUpdateRaf = requestAnimationFrame(() => {
    mapUpdateRaf = null
    if (idx === null) {
      if (heatmapEnabled.value) buildHeatLayer()
      buildRouteLines()
      fitAll()
    } else {
      removeHeatLayer()
      buildSelectedLine()
      const trip = store.trips[idx]
      if (trip) fitTrip(trip)
    }
  })
})

// Heatmap toggle while no trip is selected
watch(heatmapEnabled, (enabled) => {
  if (selectedTripIndex.value !== null) return
  if (enabled) buildHeatLayer()
  else removeHeatLayer()
})

// Speed overlay toggle while a trip is selected
watch(speedOverlayEnabled, () => {
  if (selectedTripIndex.value === null) return
  if (mapUpdateRaf !== null) cancelAnimationFrame(mapUpdateRaf)
  mapUpdateRaf = requestAnimationFrame(() => {
    mapUpdateRaf = null
    buildSelectedLine()
  })
})

// Charging stations toggle and filter changes
watch(chargingStationsEnabled, (enabled) => {
  if (enabled) loadChargingStations()
  else clearChargingMarkers()
})

watch(fuelStationsEnabled, (enabled) => {
  if (enabled) {
    loadFuelBrands()
    loadPoiLayer('fuel')
  } else {
    clearPoiMarkers('fuel')
    cachedFuelBrands.value = []
  }
})

watch(fuelBrandFilter, () => {
  if (fuelStationsEnabled.value) redrawFuelMarkers()
})

watch(serviceAreasEnabled, (enabled) => {
  if (enabled) loadPoiLayer('service_area')
  else clearPoiMarkers('service_area')
})

watch(isHev, (hev) => {
  if (hev) clearChargingMarkers()
  else if (chargingStationsEnabled.value) loadChargingStations()
})

watch(isBev, (bev) => {
  if (bev) clearPoiMarkers('fuel')
  else if (fuelStationsEnabled.value) loadPoiLayer('fuel')
})

// Vehicle type transitions from unknown once fetchConfig resolves - reload fuel layer now we know the type
watch(vehicleType, (newVt, oldVt) => {
  if (oldVt !== 'unknown' || newVt === 'unknown' || !mapInstance.value) return
  if (fuelStationsEnabled.value) {
    loadFuelBrands()
    loadPoiLayer('fuel')
  }
})

watch(chargingMinPowerKw, () => {
  if (chargingStationsEnabled.value) redrawChargingMarkers()
})
watch(chargingMaxPowerKw, () => {
  if (chargingStationsEnabled.value) redrawChargingMarkers()
})

// Route outline toggle: rebuild whichever layer is currently active
watch(routeOutlineEnabled, () => {
  if (mapUpdateRaf !== null) cancelAnimationFrame(mapUpdateRaf)
  mapUpdateRaf = requestAnimationFrame(() => {
    mapUpdateRaf = null
    if (selectedTripIndex.value === null) buildRouteLines()
    else buildSelectedLine()
  })
})

// Date range change: reload trips and reset state
watch(dateRangeDays, async (days) => {
  displayCount.value = LOAD_MORE_SIZE
  selectedTripIndex.value = null
  if (vin.value) {
    await store.fetchTrips(vin.value, new Date(Date.now() - days * 86_400_000).toISOString())
  }
})

// Trip just finished: silently prepend without resetting selection or page
watch(
  () => store.tripJustCompleted,
  async (completed) => {
    if (!completed || !vin.value) return
    await store.fetchTrips(
      vin.value,
      new Date(Date.now() - dateRangeDays.value * 86_400_000).toISOString(),
    )
  },
)

function selectTrip(realIdx: number) {
  if (selectedTripIndex.value === realIdx) {
    selectedTripIndex.value = null
    return
  }
  selectedTripIndex.value = realIdx
  nextTick(() => {
    const sidebar = tripSidebarRef.value
    const active = sidebar?.querySelector('.trip-list__item--active') as HTMLElement | null
    if (!sidebar || !active) return
    const header = sidebar.querySelector('.trip-sidebar__header') as HTMLElement | null
    const headerHeight = header?.offsetHeight ?? 0
    const sidebarRect = sidebar.getBoundingClientRect()
    const itemRect = active.getBoundingClientRect()
    const itemTop = itemRect.top - sidebarRect.top
    const itemBottom = itemRect.bottom - sidebarRect.top
    if (itemTop < headerHeight) {
      sidebar.scrollBy({ top: itemTop - headerHeight, behavior: 'smooth' })
    } else if (itemBottom > sidebarRect.height) {
      sidebar.scrollBy({ top: itemBottom - sidebarRect.height, behavior: 'smooth' })
    }
  })
}

onMounted(async () => {
  if (route.query.selectLatest) {
    router.replace({ name: 'map' })
  }
  await store.fetchVehicles()
  if (vin.value) {
    await Promise.all([
      store.fetchStatus(vin.value),
      store.fetchConfig(vin.value),
      store.fetchTrips(
        vin.value,
        new Date(Date.now() - dateRangeDays.value * 86_400_000).toISOString(),
      ),
    ])
  }
  if (fuelStationsEnabled.value) {
    loadFuelBrands()
  }
  nextTick(() => {
    if (sentinelRef.value && tripSidebarRef.value) {
      infiniteScrollObserver = new IntersectionObserver(
        ([entry]) => {
          if (entry?.isIntersecting && displayCount.value < store.trips.length) {
            displayCount.value += LOAD_MORE_SIZE
          }
        },
        { root: tripSidebarRef.value },
      )
      infiniteScrollObserver.observe(sentinelRef.value)
    }
  })
})

onUnmounted(() => {
  if (mapUpdateRaf !== null) cancelAnimationFrame(mapUpdateRaf)
  if (chargingDebounceTimer !== null) clearTimeout(chargingDebounceTimer)
  if (fuelDebounceTimer !== null) clearTimeout(fuelDebounceTimer)
  if (serviceAreaDebounceTimer !== null) clearTimeout(serviceAreaDebounceTimer)
  removeHeatLayer()
  clearChargingMarkers()
  clearPoiMarkers('fuel')
  clearPoiMarkers('service_area')
  resizeObserver?.disconnect()
  infiniteScrollObserver?.disconnect()
})
</script>

<template>
  <div class="view-container view-container--map">
    <div class="view-header">
      <h1>{{ t('nav.map') }}</h1>
      <div class="view-header__actions">
        <FiltersPanel>
          <div class="settings-toggle">
            <div class="settings-toggle__info">
              <span class="settings-toggle__label">
                <font-awesome-icon icon="calendar-check" class="settings-toggle__icon" />
                {{ t('trips.dateRange') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.dateRangeDesc') }}</span>
            </div>
            <div class="settings-toggle__control">
              <select v-model="dateRangeDays" class="form-select form-select-sm">
                <option :value="7">{{ t('trips.last7days') }}</option>
                <option :value="30">{{ t('trips.last30days') }}</option>
                <option :value="90">{{ t('trips.last90days') }}</option>
              </select>
            </div>
          </div>
          <div class="settings-toggle">
            <div class="settings-toggle__info">
              <span class="settings-toggle__label">
                <font-awesome-icon icon="fire" class="settings-toggle__icon" />
                {{ t('trips.heatmap') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.heatmapDesc') }}</span>
            </div>
            <div class="settings-toggle__control form-check form-switch">
              <input
                v-model="heatmapEnabled"
                type="checkbox"
                class="form-check-input"
                :aria-label="t('trips.heatmap')"
              />
            </div>
          </div>
          <div class="settings-toggle">
            <div class="settings-toggle__info">
              <span class="settings-toggle__label">
                <font-awesome-icon icon="route" class="settings-toggle__icon" />
                {{ t('trips.routeOutline') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.routeOutlineDesc') }}</span>
            </div>
            <div class="settings-toggle__control form-check form-switch">
              <input
                v-model="routeOutlineEnabled"
                type="checkbox"
                class="form-check-input"
                :aria-label="t('trips.routeOutline')"
              />
            </div>
          </div>
          <div class="settings-toggle">
            <div class="settings-toggle__info">
              <span class="settings-toggle__label">
                <font-awesome-icon icon="gauge" class="settings-toggle__icon" />
                {{ t('trips.speedOverlay') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.speedOverlayDesc') }}</span>
            </div>
            <div class="settings-toggle__control form-check form-switch">
              <input
                v-model="speedOverlayEnabled"
                type="checkbox"
                class="form-check-input"
                :aria-label="t('trips.speedOverlay')"
              />
            </div>
          </div>
          <div class="settings-toggle">
            <div class="settings-toggle__info">
              <span class="settings-toggle__label">
                <font-awesome-icon icon="road" class="settings-toggle__icon" />
                {{ t('trips.serviceAreas') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.serviceAreasDesc') }}</span>
            </div>
            <div class="settings-toggle__control form-check form-switch">
              <input
                v-model="serviceAreasEnabled"
                type="checkbox"
                class="form-check-input"
                :aria-label="t('trips.serviceAreas')"
              />
            </div>
          </div>
          <template v-if="!isBev && fuelStationsEnabled">
            <div class="fuel-brand-filter">
              <div class="fuel-brand-filter__header">
                <div class="settings-toggle__info">
                  <span class="settings-toggle__label">
                    <font-awesome-icon icon="tag" class="settings-toggle__icon" />
                    {{ t('trips.fuelBrandFilter') }}
                  </span>
                  <span class="settings-toggle__desc">{{ t('trips.fuelBrandFilterDesc') }}</span>
                </div>
              </div>
              <Multiselect
                v-model="fuelBrandFilter"
                :options="availableFuelBrands"
                :placeholder="t('trips.fuelBrandPlaceholder')"
                :searchable="true"
                :close-on-select="false"
                :clear-on-select="false"
                mode="tags"
                :loading="brandsLoading"
                :no-results-text="t('trips.fuelBrandNoMatch')"
                :no-options-text="t('trips.fuelBrandNoneLoaded')"
                append-to="body"
                class="fuel-brand-multiselect"
              />
            </div>
          </template>
          <template v-if="!isHev && chargingStationsEnabled">
            <div class="charging-power-filter">
              <div class="charging-power-filter__header">
                <div>
                  <span class="settings-toggle__label">
                    <font-awesome-icon icon="bolt" class="settings-toggle__icon" />
                    {{ t('trips.chargingPower') }}
                  </span>
                  <span class="settings-toggle__desc">{{ t('trips.chargingPowerDesc') }}</span>
                </div>
                <span class="charging-power-filter__range">{{ powerRangeLabel }}</span>
              </div>
              <div class="charging-power-filter__slider">
                <Slider
                  v-model="powerRangeSlider"
                  :min="0"
                  :max="350"
                  :step="10"
                  :tooltips="true"
                  :format="formatPowerTooltip"
                  :merge="50"
                  :lazy="false"
                  class="charging-slider"
                  :aria-label="[t('trips.chargingMinPower'), t('trips.chargingMaxPower')]"
                />
              </div>
            </div>
          </template>
        </FiltersPanel>
        <button
          v-if="!isBev"
          class="btn btn-sm map-layer-btn"
          :class="{ 'map-layer-btn--active map-layer-btn--fuel': fuelStationsEnabled }"
          :aria-pressed="fuelStationsEnabled"
          @click="fuelStationsEnabled = !fuelStationsEnabled"
        >
          <font-awesome-icon icon="gas-pump" />
          {{ t('trips.fuelStations') }}
        </button>
        <button
          v-if="!isHev"
          class="btn btn-sm map-layer-btn"
          :class="{ 'map-layer-btn--active map-layer-btn--charging': chargingStationsEnabled }"
          :aria-pressed="chargingStationsEnabled"
          @click="chargingStationsEnabled = !chargingStationsEnabled"
        >
          <font-awesome-icon icon="bolt" />
          {{ t('trips.chargingStations') }}
        </button>
        <button
          class="btn btn-sm btn-outline-secondary"
          :disabled="status?.latitude == null || status?.longitude == null"
          @click="flyToStatus"
        >
          <font-awesome-icon icon="location-dot" />
          {{ t('trips.findCar') }}
        </button>
      </div>
    </div>

    <div class="map-layout">
      <!-- Trip sidebar -->
      <aside ref="tripSidebarRef" class="trip-sidebar">
        <div class="trip-sidebar__header">
          <h3 class="trip-sidebar__title">{{ t('trips.title') }}</h3>
        </div>

        <div v-if="store.loading" class="trip-list">
          <div v-for="i in LOAD_MORE_SIZE" :key="i" class="trip-list__item">
            <span class="trip-list__dot skeleton" />
            <div class="trip-list__info">
              <div class="trip-list__header">
                <span class="skeleton skeleton--text skeleton--text-lg" />
                <span class="skeleton skeleton--trip-time" />
              </div>
              <span class="skeleton skeleton--text skeleton--text-md" />
            </div>
          </div>
        </div>

        <div v-else-if="!store.trips.length" class="empty-state text-sm">
          {{ t('trips.noTrips') }}
        </div>

        <ul v-else class="trip-list">
          <li
            v-for="(trip, displayIdx) in displayTrips"
            :key="displayIdx"
            class="trip-list__item"
            :class="{
              'trip-list__item--active': selectedTripIndex === realIndex(displayIdx),
            }"
            @click="selectTrip(realIndex(displayIdx))"
          >
            <span class="trip-list__dot" :class="tripColorClass(realIndex(displayIdx))" />
            <div class="trip-list__info">
              <div class="trip-list__header">
                <span
                  class="trip-list__name"
                  :title="new Date(trip.startedAt).toLocaleDateString(displayLocale)"
                  >{{ new Date(trip.startedAt).toLocaleDateString(displayLocale) }}</span
                >
                <span class="trip-list__time">{{
                  new Date(trip.startedAt).toLocaleTimeString(displayLocale, {
                    hour: '2-digit',
                    minute: '2-digit',
                  })
                }}</span>
              </div>
              <span
                class="trip-list__meta"
                :title="`${trip.distanceKm} ${t('common.km')} · ${formatDuration(trip.startedAt, trip.endedAt)} · ${trip.pointCount} ${t('trips.points')}`"
              >
                {{ trip.distanceKm }} {{ t('common.km') }} &middot;
                {{ formatDuration(trip.startedAt, trip.endedAt) }} &middot; {{ trip.pointCount }}
                {{ t('trips.points') }}
              </span>
            </div>
          </li>
        </ul>
        <div ref="sentinelRef" />
      </aside>

      <!-- Map -->
      <div ref="mapWrapperRef" class="map-wrapper">
        <LMap :zoom="13" :center="center" class="map-canvas" @ready="onMapReady">
          <LTileLayer
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
            attribution="© OpenStreetMap contributors"
          />

          <!-- Current position marker: hidden while a trip is selected -->
          <LMarker
            v-if="
              status?.latitude != null && status?.longitude != null && selectedTripIndex === null
            "
            :lat-lng="[status.latitude!, status.longitude!]"
          >
            <LPopup>{{ store.vehicles[0]?.model ?? store.vehicles[0]?.vin }}</LPopup>
          </LMarker>
        </LMap>

        <div
          v-if="poiLoading"
          class="poi-loading-indicator"
          role="status"
          :aria-label="t('trips.poiLoading')"
        >
          <span class="spinner-border spinner-border-sm" aria-hidden="true" />
          {{ t('trips.poiLoading') }}
        </div>

        <div
          v-if="speedOverlayEnabled && selectedTripIndex !== null"
          class="speed-legend"
          :aria-label="t('trips.speedOverlay')"
        >
          <div class="speed-legend__bar"></div>
          <div class="speed-legend__labels">
            <span>0</span>
            <span>50</span>
            <span>90</span>
            <span>130+ km/h</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style>
@import '@vueform/slider/themes/default.css';
@import '@vueform/multiselect/themes/default.css';

/* Leaflet injects divIcon HTML outside Vue's rendering pipeline so these cannot be scoped */

.trip-marker--start {
  width: 16px;
  height: 16px;
  background: #10b981;
  border: 3px solid #fff;
  border-radius: 50%;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.35);
  animation: trip-start-pulse 2s ease-in-out infinite;
}

@keyframes trip-start-pulse {
  0% {
    box-shadow:
      0 0 0 0 rgba(16, 185, 129, 0.55),
      0 1px 4px rgba(0, 0, 0, 0.35);
  }
  70% {
    box-shadow:
      0 0 0 10px rgba(16, 185, 129, 0),
      0 1px 4px rgba(0, 0, 0, 0.35);
  }
  100% {
    box-shadow:
      0 0 0 0 rgba(16, 185, 129, 0),
      0 1px 4px rgba(0, 0, 0, 0.35);
  }
}

.trip-marker--end {
  position: relative;
  width: 20px;
  height: 32px;
  pointer-events: none;
}

.trip-flag-pole {
  position: absolute;
  left: 1px;
  top: 0;
  width: 2px;
  height: 32px;
  background: #333;
  border-radius: 1px;
}

.trip-flag-flag {
  position: absolute;
  left: 3px;
  top: 1px;
  width: 16px;
  height: 11px;
  background: repeating-conic-gradient(#111 0% 25%, #fff 0% 50%) 0 0 / 5.33px 5.5px;
  border: 1px solid rgba(0, 0, 0, 0.4);
  transform-origin: left center;
  animation: trip-flag-wave 1.6s ease-in-out infinite;
}

@keyframes trip-flag-wave {
  0%,
  100% {
    transform: skewY(0deg) scaleX(1);
  }
  30% {
    transform: skewY(-3deg) scaleX(0.97);
  }
  70% {
    transform: skewY(3deg) scaleX(0.97);
  }
}

.charging-power-filter {
  padding: 0.25rem 0 0.5rem;
}

.charging-power-filter__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.9rem;
}

.charging-power-filter__range {
  font-size: 0.75rem;
  color: var(--color-text-muted, #888);
}

.charging-power-filter__slider {
  padding: 0 0.5rem;
}

.charging-slider {
  --slider-connect-bg: #22c55e;
  --slider-tooltip-bg: #22c55e;
  --slider-tooltip-color: #fff;
  --slider-handle-ring-color: rgba(34, 197, 94, 0.2);
}

/* --ms-* vars set on :root so they cascade to the body-teleported dropdown too */
:root {
  --ms-bg: var(--color-surface-2);
  --ms-bg-disabled: var(--color-surface);
  --ms-border-color: var(--color-border);
  --ms-border-width: 1px;
  --ms-radius: 6px;
  --ms-py: 0.375rem;
  --ms-px: 0.625rem;
  --ms-font-size: 0.875rem;
  --ms-line-height: 1.375;
  --ms-spinner-color: var(--color-primary);
  --ms-caret-color: var(--color-text-muted);
  --ms-clear-color: var(--color-text-muted);
  --ms-clear-color-hover: var(--color-text);

  --ms-tag-font-size: 0.8rem;
  --ms-tag-font-weight: 500;
  --ms-tag-line-height: 1.25;
  --ms-tag-py: 0.2rem;
  --ms-tag-px: 0.5rem;
  --ms-tag-my: 0.2rem;
  --ms-tag-mx: 0.2rem;
  --ms-tag-bg: var(--color-primary);
  --ms-tag-bg-disabled: var(--color-surface);
  --ms-tag-color: #fff;
  --ms-tag-color-disabled: var(--color-text-muted);
  --ms-tag-radius: 4px;
  --ms-tag-remove-radius: 3px;
  --ms-tag-remove-py: 0.2rem;
  --ms-tag-remove-px: 0.2rem;

  --ms-dropdown-bg: var(--color-surface);
  --ms-dropdown-border-color: var(--color-border);
  --ms-dropdown-border-width: 1px;
  --ms-dropdown-radius: 6px;

  --ms-placeholder-color: var(--color-text-muted);

  --ms-option-font-size: 0.875rem;
  --ms-option-bg-pointed: var(--color-surface-2);
  --ms-option-color-pointed: var(--color-text);
  --ms-option-bg-selected: var(--color-primary);
  --ms-option-color-selected: #fff;
  --ms-option-bg-selected-pointed: color-mix(in srgb, var(--color-primary) 85%, #000);
  --ms-option-color-selected-pointed: #fff;
  --ms-option-bg-disabled: transparent;
  --ms-option-color-disabled: var(--color-text-muted);
  --ms-option-py: 0.5rem;
  --ms-option-px: 0.75rem;

  --ms-empty-color: var(--color-text-muted);

  --ms-ring-width: 2px;
  --ms-ring-color: color-mix(in srgb, var(--color-primary) 30%, transparent);
}

/* Teleported dropdown must sit above the modal (z-index 1100) */
.multiselect-dropdown {
  z-index: 1200;
}

/* Ensure the search input inside the multiselect also uses the theme background */
.multiselect-search,
.multiselect-tags-search {
  background: var(--color-surface-2);
  color: var(--color-text);
}

.fuel-brand-filter {
  padding: 0.25rem 0 0.5rem;
}

.fuel-brand-filter__header {
  margin-bottom: 0.5rem;
}
</style>
