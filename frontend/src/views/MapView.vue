<script setup lang="ts">
import { onMounted, computed, ref, shallowRef, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { LMap, LTileLayer, LMarker, LPopup } from '@vue-leaflet/vue-leaflet'
import L, { type Map as LeafletMap } from 'leaflet'
import 'leaflet.heat'
import type { Trip } from '@/services/api'

const { t } = useI18n()
const store = useVehicleStore()

const vin = computed(() => store.vehicles[0]?.vin ?? null)
const status = computed(() => store.currentStatus)
const selectedTripIndex = ref<number | null>(null)

const mapInstance = shallowRef<LeafletMap | null>(null)
let heatLayer: L.Layer | null = null
let routeLines: L.Polyline[] = []

const tripColors = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899']

function tripColor(index: number): string {
  return tripColors[index % tripColors.length] ?? '#3b82f6'
}

// Static initial centre — map position is controlled by fitAll/fitTrip after data loads.
// Using a computed here would cause @vue-leaflet/vue-leaflet to call panTo() whenever
// status updates, overriding the fitBounds we set for trip routes.
const center: [number, number] = [52.3676, 4.9041]

const selectedTrip = computed<Trip | null>(() =>
  selectedTripIndex.value !== null ? (store.trips[selectedTripIndex.value] ?? null) : null,
)

// All points across every trip — used for heatmap and initial fit
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
  if (!map || allPoints.value.length === 0) return
  if (heatLayer) { heatLayer.remove(); heatLayer = null }
  heatLayer = L.heatLayer(allPoints.value, {
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
  fitBoundsSafe(allPoints.value)
}

function fitTrip(trip: Trip) {
  fitBoundsSafe(trip.points.map((p) => [p.latitude, p.longitude] as [number, number]))
}

function onMapReady(map: LeafletMap) {
  mapInstance.value = map
  if (allPoints.value.length > 0) {
    buildHeatLayer()
    buildRouteLines()
    fitAll()
  }
}

// When trips load after map is ready, draw heatmap + route lines and fit
watch(allPoints, async (pts) => {
  if (pts.length === 0 || !mapInstance.value) return
  await nextTick()
  buildHeatLayer()
  buildRouteLines()
  fitAll()
})

// React to trip selection
watch(selectedTripIndex, (idx) => {
  if (idx === null) {
    buildHeatLayer()
    buildRouteLines()
    fitAll()
  } else {
    removeHeatLayer()
    buildSelectedLine()
    const trip = store.trips[idx]
    if (trip) fitTrip(trip)
  }
})

function selectTrip(i: number) {
  selectedTripIndex.value = selectedTripIndex.value === i ? null : i
}

onMounted(async () => {
  await store.fetchVehicles()
  if (vin.value) {
    await Promise.all([
      store.fetchStatus(vin.value),
      store.fetchTrips(vin.value, new Date(Date.now() - 30 * 86_400_000).toISOString()),
    ])
  }
})
</script>

<template>
  <div class="view-container view-container--map">
    <div class="view-header">
      <h1>{{ t('nav.map') }}</h1>
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

        <ul v-else class="trip-list">
          <li
            v-for="(trip, i) in store.trips"
            :key="i"
            class="trip-list__item"
            :class="{ 'trip-list__item--active': selectedTripIndex === i }"
            @click="selectTrip(i)"
          >
            <span class="trip-list__dot" :style="{ background: tripColor(i) }" />
            <div class="trip-list__info">
              <span class="trip-list__name">{{ t('trips.trip') }} {{ i + 1 }}</span>
              <span class="trip-list__meta">
                {{ trip.distanceKm }} {{ t('common.km') }} &middot; {{ formatDuration(trip.startedAt, trip.endedAt) }}
              </span>
              <span class="trip-list__date">{{ new Date(trip.startedAt).toLocaleDateString() }}</span>
            </div>
          </li>
        </ul>

        <!-- Selected trip detail -->
        <div v-if="selectedTrip" class="trip-detail">
          <h4>{{ t('trips.trip') }} {{ (selectedTripIndex ?? 0) + 1 }}</h4>
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
      </aside>

      <!-- Map -->
      <div class="map-wrapper">
        <LMap :zoom="13" :center="center" style="height: 100%" @ready="onMapReady">
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
