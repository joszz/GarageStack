<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref, shallowRef, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { LMap, LTileLayer, LMarker, LPopup } from '@vue-leaflet/vue-leaflet'
import * as L from 'leaflet'
import type { Map as LeafletMap } from 'leaflet'
import 'leaflet.heat'
import type { Trip } from '@/services/api'

const { t } = useI18n()
const store = useVehicleStore()

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)
const selectedTripIndex = ref<number | null>(null)
const heatmapEnabled = ref(true)
const dateRangeDays = ref(30)
const tripsPage = ref(1)
const PAGE_SIZE = 10

const mapWrapperRef = ref<HTMLElement | null>(null)
const mapInstance = shallowRef<LeafletMap | null>(null)
let heatLayer: L.Layer | null = null
let routeLines: L.Polyline[] = []
let resizeObserver: ResizeObserver | null = null

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

// Static initial centre — controlled by fitAll/flyToStatus after data loads.
const center: [number, number] = [52.3676, 4.9041]

// Trips displayed newest-first in the sidebar; selectedTripIndex is always the real store.trips index.
const newestFirstTrips = computed(() => [...store.trips].reverse())
const pageOffset = computed(() => (tripsPage.value - 1) * PAGE_SIZE)
const displayTrips = computed(() => newestFirstTrips.value.slice(pageOffset.value, pageOffset.value + PAGE_SIZE))
const totalPages = computed(() => Math.max(1, Math.ceil(store.trips.length / PAGE_SIZE)))

function realIndex(newestFirstIdx: number): number {
  return store.trips.length - 1 - newestFirstIdx
}

const selectedTrip = computed<Trip | null>(() =>
  selectedTripIndex.value !== null ? (store.trips[selectedTripIndex.value] ?? null) : null,
)

const allPoints = computed<[number, number][]>(() =>
  store.trips.flatMap((trip) => trip.points.map((p) => [p.latitude, p.longitude] as [number, number])),
)

function formatDuration(startedAt: string, endedAt: string): string {
  const ms = new Date(endedAt).getTime() - new Date(startedAt).getTime()
  const mins = Math.round(ms / 60_000)
  if (mins < 60) return `${mins} min`
  return `${Math.floor(mins / 60)}h ${mins % 60}m`
}

function clearRouteLines() {
  routeLines.forEach(l => l.remove())
  routeLines = []
}

function buildHeatLayer() {
  const map = mapInstance.value
  if (!map || allPoints.value.length === 0 || !heatmapEnabled.value) return
  if (typeof leafWithHeat.heatLayer !== 'function') {
    console.warn('[map] leaflet.heat plugin not available')
    return
  }
  if (heatLayer) { heatLayer.remove(); heatLayer = null }
  heatLayer = leafWithHeat.heatLayer(allPoints.value, {
    radius: 18,
    blur: 22,
    maxZoom: 17,
    gradient: { 0.4: '#3b82f6', 0.65: '#f59e0b', 1.0: '#ef4444' },
  }).addTo(map)
}

function buildRouteLines() {
  const map = mapInstance.value
  if (!map) return
  clearRouteLines()
  store.trips.forEach((trip, i) => {
    const pts = trip.points.map(p => [p.latitude, p.longitude] as [number, number])
    if (pts.length < 2) return
    const line = L.polyline(pts, { color: tripColor(i), weight: 3, opacity: 0.75 })
    line.on('click', () => selectTrip(i, true))
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
  const pts = trip.points.map(p => [p.latitude, p.longitude] as [number, number])
  if (pts.length < 2) return
  const line = L.polyline(pts, { color: tripColor(idx), weight: 5, opacity: 1 })
  line.addTo(map)
  routeLines.push(line)
}

function removeHeatLayer() {
  if (heatLayer) { heatLayer.remove(); heatLayer = null }
}

function fitBoundsSafe(pts: [number, number][]) {
  const map = mapInstance.value
  if (!map || pts.length === 0) return
  const bounds = L.latLngBounds(pts)
  if (bounds.getNorthEast().equals(bounds.getSouthWest())) {
    map.setView(bounds.getCenter(), 15)
  } else {
    map.fitBounds(bounds, { padding: [32, 32] })
  }
}

function fitAll() {
  const pts = [...allPoints.value]
  if (status.value?.latitude && status.value?.longitude) {
    pts.push([status.value.latitude, status.value.longitude])
  }
  fitBoundsSafe(pts)
}

function fitTrip(trip: Trip) {
  fitBoundsSafe(trip.points.map((p) => [p.latitude, p.longitude] as [number, number]))
}

function flyToStatus() {
  const map = mapInstance.value
  const s = status.value
  if (map && s?.latitude && s?.longitude) {
    map.setView([s.latitude, s.longitude], 14)
  }
}

function onMapReady(map: LeafletMap) {
  mapInstance.value = map

  // Fix mobile blank map: wait for layout to settle so Leaflet gets the real container size
  nextTick(() => {
    map.invalidateSize()
    if (allPoints.value.length > 0) {
      buildHeatLayer()
      buildRouteLines()
      fitAll()
    } else if (status.value?.latitude && status.value?.longitude) {
      map.setView([status.value.latitude, status.value.longitude], 14)
    }
  })

  // Keep map in sync when the flex container resizes (e.g. orientation change on mobile)
  if (mapWrapperRef.value) {
    resizeObserver = new ResizeObserver(() => map.invalidateSize())
    resizeObserver.observe(mapWrapperRef.value)
  }
}

// When status loads and there are no trip points yet, fly to the car's position
watch(status, (s) => {
  if (!mapInstance.value || !s?.latitude || !s?.longitude) return
  if (allPoints.value.length === 0) {
    mapInstance.value.setView([s.latitude, s.longitude], 14)
  }
})

// When trips load after the map is ready, rebuild layers and fit
watch(allPoints, async (pts) => {
  if (pts.length === 0 || !mapInstance.value) return
  await nextTick()
  buildHeatLayer()
  buildRouteLines()
  fitAll()
})

// Trip selection drives map display
watch(selectedTripIndex, (idx) => {
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

// Heatmap toggle while no trip is selected
watch(heatmapEnabled, (enabled) => {
  if (selectedTripIndex.value !== null) return
  if (enabled) buildHeatLayer()
  else removeHeatLayer()
})

// Date range change: reload trips and reset state
watch(dateRangeDays, async (days) => {
  tripsPage.value = 1
  selectedTripIndex.value = null
  if (vin.value) {
    await store.fetchTrips(vin.value, new Date(Date.now() - days * 86_400_000).toISOString())
  }
})

// Deselect when paginating
watch(tripsPage, () => {
  selectedTripIndex.value = null
})

// Select a trip by its real store index; if called from the map line, also jump the sidebar page
function selectTrip(realIdx: number, fromMap = false) {
  if (selectedTripIndex.value === realIdx) {
    selectedTripIndex.value = null
    return
  }
  selectedTripIndex.value = realIdx
  if (fromMap) {
    // Scroll sidebar to show this trip in newest-first order
    const newestFirstIdx = store.trips.length - 1 - realIdx
    tripsPage.value = Math.floor(newestFirstIdx / PAGE_SIZE) + 1
  }
}

onMounted(async () => {
  await store.fetchVehicles()
  if (vin.value) {
    await Promise.all([
      store.fetchStatus(vin.value),
      store.fetchTrips(vin.value, new Date(Date.now() - dateRangeDays.value * 86_400_000).toISOString()),
    ])
  }
})

onUnmounted(() => {
  resizeObserver?.disconnect()
})
</script>

<template>
  <div class="view-container view-container--map">
    <div class="view-header">
      <h1>{{ t('nav.map') }}</h1>
      <div class="view-header__actions map-controls">
        <label class="map-controls__label">{{ t('trips.dateRange') }}</label>
        <select v-model="dateRangeDays" class="form-select form-select-sm">
          <option :value="7">{{ t('trips.last7days') }}</option>
          <option :value="30">{{ t('trips.last30days') }}</option>
          <option :value="90">{{ t('trips.last90days') }}</option>
        </select>
        <button
          class="btn btn-sm"
          :class="heatmapEnabled ? 'btn-primary' : 'btn-outline-secondary'"
          @click="heatmapEnabled = !heatmapEnabled"
        >
          <font-awesome-icon icon="fire" />
          {{ t('trips.heatmap') }}
        </button>
        <button
          v-if="status?.latitude && status?.longitude"
          class="btn btn-sm btn-outline-secondary"
          @click="flyToStatus"
        >
          <font-awesome-icon icon="location-dot" />
          {{ t('trips.findCar') }}
        </button>
      </div>
    </div>

    <div class="map-layout">
      <!-- Trip sidebar -->
      <aside class="trip-sidebar">
        <h3 class="trip-sidebar__title">{{ t('trips.title') }}</h3>

        <div v-if="store.loading" class="loading-state">
          <font-awesome-icon icon="spinner" spin />
        </div>

        <div v-else-if="!store.trips.length" class="empty-state text-sm">
          {{ t('trips.noTrips') }}
        </div>

        <template v-else>
          <ul class="trip-list">
            <li
              v-for="(trip, displayIdx) in displayTrips"
              :key="pageOffset + displayIdx"
              class="trip-list__item"
              :class="{ 'trip-list__item--active': selectedTripIndex === realIndex(pageOffset + displayIdx) }"
              @click="selectTrip(realIndex(pageOffset + displayIdx))"
            >
              <span class="trip-list__dot" :class="tripColorClass(realIndex(pageOffset + displayIdx))" />
              <div class="trip-list__info">
                <span class="trip-list__name">{{ new Date(trip.startedAt).toLocaleDateString() }}</span>
                <span class="trip-list__meta">
                  {{ trip.distanceKm }} {{ t('common.km') }} &middot; {{ formatDuration(trip.startedAt, trip.endedAt) }}
                </span>
              </div>
            </li>
          </ul>

          <!-- Pagination controls -->
          <div v-if="totalPages > 1" class="trip-pagination">
            <button
              class="btn btn-sm btn-outline-secondary trip-pagination__btn"
              :disabled="tripsPage === 1"
              @click="tripsPage--"
            >
              <font-awesome-icon icon="chevron-left" />
            </button>
            <span class="trip-pagination__info">{{ tripsPage }} / {{ totalPages }}</span>
            <button
              class="btn btn-sm btn-outline-secondary trip-pagination__btn"
              :disabled="tripsPage === totalPages"
              @click="tripsPage++"
            >
              <font-awesome-icon icon="chevron-right" />
            </button>
          </div>

          <!-- Selected trip detail -->
          <div v-if="selectedTrip" class="trip-detail">
            <dl class="trip-detail__dl">
              <dt>{{ t('trips.started') }}</dt>
              <dd>{{ new Date(selectedTrip.startedAt).toLocaleString() }}</dd>
              <dt>{{ t('trips.distance') }}</dt>
              <dd>{{ selectedTrip.distanceKm }} {{ t('common.km') }}</dd>
              <dt>{{ t('trips.duration') }}</dt>
              <dd>{{ formatDuration(selectedTrip.startedAt, selectedTrip.endedAt) }}</dd>
              <dt>{{ t('trips.points') }}</dt>
              <dd>{{ selectedTrip.pointCount }}</dd>
            </dl>
          </div>
        </template>
      </aside>

      <!-- Map -->
      <div ref="mapWrapperRef" class="map-wrapper">
        <LMap :zoom="13" :center="center" class="map-canvas" @ready="onMapReady">
          <LTileLayer
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
            attribution="© OpenStreetMap contributors"
          />

          <!-- Current position marker -->
          <LMarker v-if="status?.latitude && status?.longitude" :lat-lng="[status.latitude, status.longitude]">
            <LPopup>{{ store.vehicles[0]?.model ?? store.vehicles[0]?.vin }}</LPopup>
          </LMarker>
        </LMap>
      </div>
    </div>
  </div>
</template>
