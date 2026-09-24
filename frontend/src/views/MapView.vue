<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref, watch, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { LMap, LMarker, LPopup } from '@vue-leaflet/vue-leaflet'
import FiltersPanel from '@/components/FiltersPanel.vue'
import SettingsToggle from '@/components/SettingsToggle.vue'
import { useInfiniteScroll } from '@/composables/useInfiniteScroll'
import { usePoiLayers } from '@/composables/usePoiLayers'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import { useReverseGeocode } from '@/composables/useReverseGeocode'
import { addressLabel, cityName } from '@/utils/places'
import { buildTripRow } from '@/utils/tripRows'
import type { GeoPoint } from '@/services/mapApi'
import Slider from '@vueform/slider'
import Multiselect from '@vueform/multiselect'
import { L, type LeafletMap } from '@/utils/leaflet'
import 'leaflet.heat'
import '@/assets/map.css'
import type { Trip } from '@/services/vehicleApi'
import { buildCarMarkerIcon } from '@/utils/mapCarIcon'
import { daysAgoIso } from '@/utils/dates'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const store = useVehicleStore()
const settingsStore = useMapSettingsStore()
const uiSettingsStore = useUiSettingsStore()

const vin = computed(() => store.activeVin)
const status = computed(() => store.currentStatus)
const vehicleType = computed(() => store.effectiveVehicleType)
const isHev = computed(() => vehicleType.value === 'hev')
const isBev = computed(() => vehicleType.value === 'bev')
// Until vehicleType resolves from 'unknown', neither flag is true, which would show
// both layer-toggle buttons and then remove one once the type is known - a visible
// layout shift. Keep both hidden while unknown so the header never shows more
// buttons than its final, resolved state.
const vehicleTypeKnown = computed(() => vehicleType.value !== 'unknown')
const displayLocale = computed(() => (uiSettingsStore.locale === 'nl' ? 'nl-NL' : 'en-US'))
const selectedTripIndex = ref<number | null>(null)
// Plain refs on their stores already (Composition-API-style defineStore) - storeToRefs gives
// directly writable, reactive bindings with no computed({get, set}) wrapper needed.
const { heatmapEnabled, speedOverlayEnabled, routeOutlineEnabled } = storeToRefs(settingsStore)
const { filterDays: dateRangeDays } = storeToRefs(uiSettingsStore)

let shouldSelectLatest = route.query.selectLatest === '1'
const LOAD_MORE_SIZE = 10

const mapWrapperRef = ref<HTMLElement | null>(null)
const tripSidebarRef = ref<HTMLElement | null>(null)
const { mapInstance, bindMapReady } = useLeafletMap(mapWrapperRef)

// Vector basemap in the tile pane: follows the theme and the UI language, and everything below
// draws on top of it exactly as it did over the raster tiles.
useBasemap(mapInstance)

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
  loadLayers,
} = usePoiLayers({ mapInstance, vehicleType, isHev, isBev })

let heatLayer: L.Layer | null = null
let routeLines: L.Polyline[] = []
let startMarker: L.Marker | null = null
let endMarker: L.Marker | null = null
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

// ── Place names ────────────────────────────────────────────────────────────────
const { requestPlaces, placeFor, placeNamesEnabled, placesResolving } = useReverseGeocode()

function tripEnds(trip: Trip) {
  return { start: trip.points[0], end: trip.points[trip.points.length - 1] }
}

// Only the rows the sidebar actually shows are looked up: a 90-day range would otherwise queue
// hundreds of lookups for trips nobody has scrolled to. The list grows, so this grows with it.
// The trip being driven contributes only its origin, because its last point is the car's live
// position: asking about that as it moves would be a lookup every few hundred metres.
const visibleTripEnds = computed<GeoPoint[]>(() =>
  displayTrips.value.flatMap((trip, displayIdx) => {
    const { start, end } = tripEnds(trip)
    const wanted = realIndex(displayIdx) === activeTripIndex.value ? [start] : [start, end]
    return wanted
      .filter((p): p is NonNullable<typeof p> => p != null)
      .map((p) => ({ lat: p.latitude, lng: p.longitude }))
  }),
)

watch(visibleTripEnds, (points) => requestPlaces(points, 'city'), { immediate: true })

// The car's own street and city, for its popup. Only while parked: on the move this would ask
// for a new address on every position update, and name a street the car has already left.
const parkedCarLatLng = computed<GeoPoint | null>(() => {
  const s = status.value
  if (activeTripIndex.value !== null) return null
  return s?.latitude != null && s?.longitude != null ? { lat: s.latitude, lng: s.longitude } : null
})

watch(
  parkedCarLatLng,
  (point) => {
    if (point) requestPlaces([point], 'address')
  },
  { immediate: true },
)

const carAddress = computed(() =>
  addressLabel(placeFor(parkedCarLatLng.value?.lat, parkedCarLatLng.value?.lng, 'address')),
)

// One row model per visible trip, paired with the store index selection works on.
const displayRows = computed(() =>
  displayTrips.value.map((trip, displayIdx) => {
    const { start, end } = tripEnds(trip)
    const realIdx = realIndex(displayIdx)
    return {
      realIdx,
      row: buildTripRow(trip, {
        fromCity: cityName(placeFor(start?.latitude, start?.longitude, 'city')),
        toCity: cityName(placeFor(end?.latitude, end?.longitude, 'city')),
        inProgress: realIdx === activeTripIndex.value,
        canResolve: start != null && end != null && placeNamesEnabled.value,
        resolving: placesResolving.value,
        locale: displayLocale.value,
        t,
      }),
    }
  }),
)

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
    .heatLayer(downsample(allPoints.value, MAX_HEATMAP_POINTS), {
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

function downsample<T>(items: T[], max: number): T[] {
  if (items.length <= max) return items
  const stride = items.length / max
  const sampled: T[] = []
  for (let i = 0; i < max; i++) {
    sampled.push(items[Math.floor(i * stride)]!)
  }
  const last = items[items.length - 1]!
  if (sampled[sampled.length - 1] !== last) sampled.push(last)
  return sampled
}

// Speed overlay renders one Leaflet polyline layer per segment. A long trip can have thousands
// of GPS points, which would create thousands of DOM elements - downsample first so the map
// stays responsive; a few hundred segments is already more color resolution than is visible.
const MAX_SPEED_OVERLAY_SEGMENTS = 500

// Heatmap renders to a canvas rather than one DOM element per point, so it tolerates far more
// points than the polyline overlay, but a 90-day range with dense GPS logging can still reach
// tens of thousands of points across all trips - cap it so it stays responsive to rebuild.
const MAX_HEATMAP_POINTS = 5000

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
    const speedPts = downsample(pts, MAX_SPEED_OVERLAY_SEGMENTS)
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
  // POI layer reload on pan/zoom is handled internally by usePoiLayers (it watches mapInstance).
  bindMapReady(map, () => {
    if (allPoints.value.length > 0) {
      buildHeatLayer()
      buildRouteLines()
      fitAll()
    } else if (status.value?.latitude != null && status.value?.longitude != null) {
      hasCenteredOnStatus = true
      map.setView([status.value.latitude, status.value.longitude], 14, { animate: false })
    }
    loadLayers(100)
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
    await store.fetchTrips(vin.value, daysAgoIso(days))
  }
})

// Trip just finished: silently prepend without resetting selection or page
watch(
  () => store.tripJustCompleted,
  async (completed) => {
    if (!completed || !vin.value) return
    await store.fetchTrips(vin.value, daysAgoIso(dateRangeDays.value))
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
      store.fetchTrips(vin.value, daysAgoIso(dateRangeDays.value)),
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
            v-for="{ row, realIdx } in displayRows"
            :key="realIdx"
            class="trip-list__item"
            :class="{ 'trip-list__item--active': selectedTripIndex === realIdx }"
            @click="selectTrip(realIdx)"
          >
            <span
              class="trip-list__dot"
              :class="[
                tripColorClass(realIdx),
                { 'trip-list__dot--live': realIdx === activeTripIndex },
              ]"
            />
            <div class="trip-list__info">
              <div class="trip-list__header">
                <span
                  v-if="row.mode === 'route'"
                  class="trip-list__name trip-list__route"
                  :title="row.routeTitle"
                >
                  <span class="trip-list__place">{{ row.from }}</span>
                  <template v-if="row.to">
                    <font-awesome-icon icon="arrow-right" class="trip-list__route-arrow" />
                    <span class="trip-list__place">{{ row.to }}</span>
                  </template>
                </span>
                <span
                  v-else-if="row.mode === 'pending'"
                  class="skeleton skeleton--text skeleton--text-lg trip-list__name-skeleton"
                  role="status"
                  :aria-label="t('trips.resolvingPlaces')"
                />
                <span v-else class="trip-list__name" :title="row.dateLabel">{{
                  row.dateLabel
                }}</span>
                <span v-if="realIdx === activeTripIndex" class="trip-list__live-badge">{{
                  t('trips.inProgress')
                }}</span>
                <span v-else class="trip-list__time">{{ row.timeLabel }}</span>
              </div>
              <span class="trip-list__meta" :title="row.metaTitle">{{ row.meta }}</span>
            </div>
          </li>
        </ul>
        <div ref="sentinelRef" />
      </aside>

      <!-- Map -->
      <div ref="mapWrapperRef" class="map-wrapper">
        <LMap :zoom="13" :center="center" class="map-canvas" @ready="onMapReady">
          <!-- Current position marker: hidden while a trip is selected -->
          <LMarker
            v-if="
              status?.latitude != null && status?.longitude != null && selectedTripIndex === null
            "
            :lat-lng="[status.latitude!, status.longitude!]"
          >
            <LPopup>
              <strong class="car-popup__title">{{
                store.activeVehicle?.model ?? store.activeVin
              }}</strong>
              <span v-if="carAddress" class="car-popup__address">{{ carAddress }}</span>
            </LPopup>
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

<!-- Global on purpose: Leaflet builds marker and popup HTML itself, outside Vue's renderer,
     so those elements never carry a scope attribute. Everything the template owns is scoped. -->
<style>
@import url('@vueform/slider/themes/default.css');

.trip-marker--start {
  width: 16px;
  height: 16px;
  background: #10b981;
  border: 3px solid #fff;
  border-radius: 50%;
  box-shadow: 0 1px 4px rgb(0 0 0 / 35%);
  animation: trip-start-pulse 2s ease-in-out infinite;
}

@keyframes trip-start-pulse {
  0% {
    box-shadow:
      0 0 0 0 rgb(16 185 129 / 55%),
      0 1px 4px rgb(0 0 0 / 35%);
  }

  70% {
    box-shadow:
      0 0 0 10px rgb(16 185 129 / 0%),
      0 1px 4px rgb(0 0 0 / 35%);
  }

  100% {
    box-shadow:
      0 0 0 0 rgb(16 185 129 / 0%),
      0 1px 4px rgb(0 0 0 / 35%);
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
  border: 1px solid rgb(0 0 0 / 40%);
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
</style>

<style scoped>
.trip-list__dot--live {
  animation: trip-start-pulse 2s ease-in-out infinite;
}

.trip-list__live-badge {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-success, #10b981);

  /* Never squeezed by a long route headline beside it; the city names truncate instead. */
  flex-shrink: 0;
  white-space: nowrap;
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
  --slider-handle-ring-color: rgb(34 197 94 / 20%);
}

/* Multiselect theme vars, dropdown z-index, and search input styling now live globally in
   main.css (moved there so the AppFooter settings-modal notification-type multiselect is
   themed correctly even before this lazy-loaded view has ever been visited). */

.fuel-brand-filter {
  padding: 0.25rem 0 0.5rem;
}

.fuel-brand-filter__header {
  margin-bottom: 0.5rem;
}
</style>
