<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref, shallowRef, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import type { VehicleType } from '@/stores/vehicle'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { LMap, LTileLayer, LMarker, LPopup } from '@vue-leaflet/vue-leaflet'
import FiltersPanel from '@/components/FiltersPanel.vue'
import SettingsToggle from '@/components/SettingsToggle.vue'
import { useInfiniteScroll } from '@/composables/useInfiniteScroll'
import { usePoiLayers } from '@/composables/usePoiLayers'
import Slider from '@vueform/slider'
import Multiselect from '@vueform/multiselect'
import * as LModule from 'leaflet'
import type { Map as LeafletMap } from 'leaflet'
import 'leaflet.heat'
import '@/assets/map.css'
import type { Trip } from '@/services/vehicleApi'
import { CAR_SILHOUETTE_VIEWBOX, CAR_SILHOUETTE_MARKUP } from '@/assets/carSilhouette'

// Vite wraps CJS modules in a frozen ESM namespace - `import * as LModule` gives that frozen
// namespace. leaflet.heat patches the actual mutable CJS export (LModule.default), so we must
// use that reference to reach heatLayer at runtime.
const L = ((LModule as unknown as { default?: typeof LModule }).default ??
  LModule) as typeof LModule

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const store = useVehicleStore()
const settingsStore = useMapSettingsStore()
const uiSettingsStore = useUiSettingsStore()

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)
const vehicleType = computed((): VehicleType | 'unknown' => {
  const override = uiSettingsStore.vehicleTypeOverride
  if (override !== 'auto') return override as VehicleType
  return store.detectedVehicleType
})
const isHev = computed(() => vehicleType.value === 'hev')
const isBev = computed(() => vehicleType.value === 'bev')
// Until vehicleType resolves from 'unknown', neither flag is true, which would show
// both layer-toggle buttons and then remove one once the type is known - a visible
// layout shift. Keep both hidden while unknown so the header never shows more
// buttons than its final, resolved state.
const vehicleTypeKnown = computed(() => vehicleType.value !== 'unknown')
const displayLocale = computed(() => (uiSettingsStore.locale === 'nl' ? 'nl-NL' : 'en-US'))
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

let shouldSelectLatest = route.query.selectLatest === '1'

const dateRangeDays = computed({
  get: () => uiSettingsStore.filterDays,
  set: (v: number) => {
    uiSettingsStore.filterDays = v
  },
})
const LOAD_MORE_SIZE = 10

const mapWrapperRef = ref<HTMLElement | null>(null)
const tripSidebarRef = ref<HTMLElement | null>(null)
const mapInstance = shallowRef<LeafletMap | null>(null)

// Charging-station / fuel-station / service-area layers: settings bindings, on-demand tile
// fetching/caching, marker clustering, and popups all live in this composable so this view only
// has to wire up the returned bindings and trigger the initial load once the map is ready.
const {
  chargingStationsEnabled,
  fuelStationsEnabled,
  serviceAreasEnabled,
  fuelBrandFilter,
  powerRangeSlider,
  powerRangeLabel,
  formatPowerTooltip,
  availableFuelBrands,
  brandsLoading,
  poiLoading,
  loadFuelBrands,
  loadChargingStations,
  loadPoiLayer,
} = usePoiLayers({ mapInstance, vehicleType, isHev, isBev })

let heatLayer: L.Layer | null = null
let routeLines: L.Polyline[] = []
let startMarker: L.Marker | null = null
let endMarker: L.Marker | null = null
let resizeObserver: ResizeObserver | null = null
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

const {
  displayItems: displayTrips,
  sentinelRef,
  reset: resetTripScroll,
  observe: observeTrips,
} = useInfiniteScroll(newestFirstTrips, LOAD_MORE_SIZE)

function realIndex(newestFirstIdx: number): number {
  return store.trips.length - 1 - newestFirstIdx
}

const allPoints = computed<[number, number][]>(() =>
  store.trips.flatMap((trip) =>
    trip.points.map((p) => [p.latitude, p.longitude] as [number, number]),
  ),
)

// The backend always appends the still-open segment as the last trip while the car hasn't
// been parked for 5+ minutes, so a positive currentJourneyDistance means that last trip is
// the one currently being driven.
const activeTripIndex = computed<number | null>(() => {
  if (store.trips.length === 0) return null
  const dist = status.value?.currentJourneyDistance
  if (dist == null || dist <= 0) return null
  return store.trips.length - 1
})

const activeCarLatLng = computed<[number, number] | null>(() => {
  const s = status.value
  if (s?.latitude != null && s?.longitude != null) return [s.latitude, s.longitude]
  const idx = activeTripIndex.value
  const trip = idx !== null ? store.trips[idx] : undefined
  const last = trip?.points[trip.points.length - 1]
  return last ? [last.latitude, last.longitude] : null
})

const activeCarHeading = computed(() => status.value?.heading ?? 0)

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

// Speed overlay renders one Leaflet polyline layer per segment. A long trip can have thousands
// of GPS points, which would create thousands of DOM elements - downsample first so the map
// stays responsive; a few hundred segments is already more color resolution than is visible.
const MAX_SPEED_OVERLAY_SEGMENTS = 500

function downsampleForOverlay(pts: Trip['points']): Trip['points'] {
  if (pts.length <= MAX_SPEED_OVERLAY_SEGMENTS) return pts
  const stride = pts.length / MAX_SPEED_OVERLAY_SEGMENTS
  const sampled: Trip['points'] = []
  for (let i = 0; i < MAX_SPEED_OVERLAY_SEGMENTS; i++) {
    sampled.push(pts[Math.floor(i * stride)]!)
  }
  const last = pts[pts.length - 1]!
  if (sampled[sampled.length - 1] !== last) sampled.push(last)
  return sampled
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
    const speedPts = downsampleForOverlay(pts)
    for (let i = 0; i < speedPts.length - 1; i++) {
      const p0 = speedPts[i]!
      const p1 = speedPts[i + 1]!
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

  buildTripMarkers(trip, idx)
}

// Live car icon used for a trip's endpoint while it is still in progress, in place of the
// static end flag - rotated to the vehicle's current heading (0deg = north, matching the
// silhouette's default forward-facing-up orientation).
function buildCarMarkerIcon(headingDeg: number) {
  const heading = Number.isFinite(headingDeg) ? headingDeg : 0
  return L.divIcon({
    className: '',
    html: `<div class="trip-marker trip-marker--active" style="transform: rotate(${heading}deg)">
      <svg viewBox="${CAR_SILHOUETTE_VIEWBOX}" class="trip-marker-car-svg">${CAR_SILHOUETTE_MARKUP}</svg>
    </div>`,
    iconSize: [24, 46],
    iconAnchor: [12, 23],
  })
}

function buildTripMarkers(trip: Trip, realIdx: number) {
  const map = mapInstance.value
  if (!map || trip.points.length === 0) return

  const start = trip.points[0]
  if (!start) return

  const startIcon = L.divIcon({
    className: '',
    html: '<div class="trip-marker trip-marker--start"></div>',
    iconSize: [16, 16],
    iconAnchor: [8, 8],
  })
  startMarker = L.marker([start.latitude, start.longitude], { icon: startIcon }).addTo(map)

  if (realIdx === activeTripIndex.value && activeCarLatLng.value) {
    endMarker = L.marker(activeCarLatLng.value, {
      icon: buildCarMarkerIcon(activeCarHeading.value),
    }).addTo(map)
    return
  }

  const end = trip.points[trip.points.length - 1]
  if (!end) return

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

  // POI layer reload on pan/zoom is handled internally by usePoiLayers (it watches mapInstance).

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

// Keep the active trip's car marker following the live position/heading while it's the
// selected trip, without a full rebuild (would reset the speed-overlay/route-outline state).
watch(status, (s) => {
  if (!endMarker || selectedTripIndex.value === null) return
  if (selectedTripIndex.value !== activeTripIndex.value) return
  if (s?.latitude == null || s?.longitude == null) return
  endMarker.setLatLng([s.latitude, s.longitude])
  endMarker.setIcon(buildCarMarkerIcon(s.heading ?? 0))
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
  resetTripScroll()
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
    observeTrips(tripSidebarRef.value)
  })
})

onUnmounted(() => {
  if (mapUpdateRaf !== null) cancelAnimationFrame(mapUpdateRaf)
  removeHeatLayer()
  resizeObserver?.disconnect()
  // POI layer cleanup (debouncers, marker clearing) is handled internally by usePoiLayers.
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
          <SettingsToggle v-model="heatmapEnabled" :label="t('trips.heatmap')">
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="fire" class="settings-toggle__icon" />
                {{ t('trips.heatmap') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.heatmapDesc') }}</span>
            </template>
          </SettingsToggle>
          <SettingsToggle v-model="routeOutlineEnabled" :label="t('trips.routeOutline')">
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="route" class="settings-toggle__icon" />
                {{ t('trips.routeOutline') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.routeOutlineDesc') }}</span>
            </template>
          </SettingsToggle>
          <SettingsToggle v-model="speedOverlayEnabled" :label="t('trips.speedOverlay')">
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="gauge" class="settings-toggle__icon" />
                {{ t('trips.speedOverlay') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.speedOverlayDesc') }}</span>
            </template>
          </SettingsToggle>
          <SettingsToggle v-model="serviceAreasEnabled" :label="t('trips.serviceAreas')">
            <template #label>
              <span class="settings-toggle__label">
                <font-awesome-icon icon="road" class="settings-toggle__icon" />
                {{ t('trips.serviceAreas') }}
              </span>
              <span class="settings-toggle__desc">{{ t('trips.serviceAreasDesc') }}</span>
            </template>
          </SettingsToggle>
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
          v-if="vehicleTypeKnown && !isBev"
          class="btn btn-sm map-layer-btn"
          :class="{ 'map-layer-btn--active map-layer-btn--fuel': fuelStationsEnabled }"
          :aria-pressed="fuelStationsEnabled"
          @click="fuelStationsEnabled = !fuelStationsEnabled"
        >
          <font-awesome-icon icon="gas-pump" />
          {{ t('trips.fuelStations') }}
        </button>
        <button
          v-if="vehicleTypeKnown && !isHev"
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
          <h2 class="trip-sidebar__title">{{ t('trips.title') }}</h2>
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
            <span
              class="trip-list__dot"
              :class="[
                tripColorClass(realIndex(displayIdx)),
                { 'trip-list__dot--live': realIndex(displayIdx) === activeTripIndex },
              ]"
            />
            <div class="trip-list__info">
              <div class="trip-list__header">
                <span
                  class="trip-list__name"
                  :title="new Date(trip.startedAt).toLocaleDateString(displayLocale)"
                  >{{ new Date(trip.startedAt).toLocaleDateString(displayLocale) }}</span
                >
                <span
                  v-if="realIndex(displayIdx) === activeTripIndex"
                  class="trip-list__live-badge"
                  >{{ t('trips.inProgress') }}</span
                >
                <span v-else class="trip-list__time">{{
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

.trip-marker--active {
  width: 24px;
  height: 46px;
  pointer-events: none;
  transform-origin: center;
  filter: drop-shadow(0 1px 3px rgba(0, 0, 0, 0.5));
}

.trip-marker-car-svg {
  width: 100%;
  height: 100%;
  display: block;
}

.trip-list__dot--live {
  animation: trip-start-pulse 2s ease-in-out infinite;
}

.trip-list__live-badge {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-success, #10b981);
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
