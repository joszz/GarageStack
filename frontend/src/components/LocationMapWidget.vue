<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { LMap, LTileLayer, LMarker } from '@vue-leaflet/vue-leaflet'
import { L, type LeafletMap } from '@/utils/leaflet'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useVehicleStore } from '@/stores/vehicle'
import CardInfoWrap from './CardInfoWrap.vue'
import { buildCarMarkerIcon } from '@/utils/mapCarIcon'

const { t } = useI18n()
const router = useRouter()
const store = useVehicleStore()

const status = computed(() => store.currentStatus)
const hasLocation = computed(
  () => status.value?.latitude != null && status.value?.longitude != null,
)
const center = computed<[number, number]>(() => [
  status.value?.latitude ?? 0,
  status.value?.longitude ?? 0,
])

// Same logic as MapView: the backend always appends the still-open segment as the last trip
// while the car hasn't parked for 5+ minutes, so a positive currentJourneyDistance means that
// last trip is the one currently being driven.
const activeTrip = computed(() => {
  const dist = status.value?.currentJourneyDistance
  if (dist == null || dist <= 0 || store.trips.length === 0) return null
  return store.trips[store.trips.length - 1] ?? null
})

const mapOptions = {
  zoomControl: false,
  dragging: false,
  scrollWheelZoom: false,
  doubleClickZoom: false,
  touchZoom: false,
  boxZoom: false,
  keyboard: false,
}

const mapWrapperRef = ref<HTMLElement | null>(null)
const { mapInstance, bindMapReady } = useLeafletMap(mapWrapperRef)
let routeLine: L.Polyline | null = null
let carMarker: L.Marker | null = null

function clearActiveTripLayers() {
  routeLine?.remove()
  routeLine = null
  carMarker?.remove()
  carMarker = null
}

// Draws the in-progress trip's route (as recorded so far) plus a live car marker at the
// current position/heading, in place of the plain pin shown while parked.
function buildActiveTripLayers() {
  const map = mapInstance.value
  const trip = activeTrip.value
  clearActiveTripLayers()
  if (!map || !trip || !hasLocation.value) return

  if (trip.points.length >= 2) {
    const coords = trip.points.map((p) => [p.latitude, p.longitude] as [number, number])
    routeLine = L.polyline(coords, { color: '#3b82f6', weight: 4, opacity: 0.8 }).addTo(map)
  }
  carMarker = L.marker(center.value, {
    icon: buildCarMarkerIcon(status.value?.heading ?? 0),
  }).addTo(map)
}

// While a trip is in progress, fit the view to the entire route travelled so far (start to
// current position) instead of just centering on the car, so the whole trip stays visible as
// it grows. Falls back to centering on the car when parked (no active trip).
function updateMapView() {
  const map = mapInstance.value
  if (!map) return
  const trip = activeTrip.value
  if (trip) {
    const coords = trip.points.map((p) => [p.latitude, p.longitude] as [number, number])
    if (hasLocation.value) coords.push(center.value)
    if (coords.length === 0) return
    const bounds = L.latLngBounds(coords)
    if (bounds.getNorthEast().equals(bounds.getSouthWest())) {
      map.setView(bounds.getCenter(), 15, { animate: false })
    } else {
      map.fitBounds(bounds, { padding: [24, 24], animate: false })
    }
  } else if (hasLocation.value) {
    map.setView(center.value, 14, { animate: false })
  }
}

function onMapReady(map: LeafletMap) {
  bindMapReady(map, () => {
    buildActiveTripLayers()
    updateMapView()
  })
}

// Keep the preview following the car (or the whole in-progress trip) as new status updates
// arrive, and keep the live car marker following position/heading without a full layer rebuild.
watch(status, (s) => {
  if (!mapInstance.value || s?.latitude == null || s?.longitude == null) return
  if (carMarker) {
    carMarker.setLatLng([s.latitude, s.longitude])
    carMarker.setIcon(buildCarMarkerIcon(s.heading ?? 0))
  }
  updateMapView()
})

// Trip start/end, or a refreshed trips fetch: rebuild the route line and car marker, and
// refit the view to the (possibly grown) route.
watch(activeTrip, () => {
  buildActiveTripLayers()
  updateMapView()
})

onUnmounted(() => {
  clearActiveTripLayers()
})

function openFullMap() {
  // Same deep-link MapView already supports for the "active trip" dashboard card - it
  // auto-selects the last trip in the list, which is the in-progress one when there is one.
  if (activeTrip.value) {
    router.push({ name: 'map', query: { selectLatest: '1' } })
  } else {
    router.push({ name: 'map' })
  }
}
</script>

<template>
  <CardInfoWrap :title="t('vehicle.location')" :description="t('dashboard.cardDesc.location')">
    <div class="location-map-card">
      <p class="tyre-diagram__title">
        <font-awesome-icon icon="location-dot" />
        {{ t('vehicle.location') }}
      </p>

      <div ref="mapWrapperRef" class="location-map-card__map" @click="openFullMap">
        <LMap
          v-if="hasLocation"
          :zoom="14"
          :center="center"
          :options="mapOptions"
          class="location-map-card__canvas"
          @ready="onMapReady"
        >
          <LTileLayer
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
            attribution="© OpenStreetMap contributors"
          />
          <LMarker v-if="!activeTrip" :lat-lng="center" />
        </LMap>
        <div v-else class="location-map-card__empty">
          <font-awesome-icon icon="location-dot" />
          <span>{{ t('vehicle.locationUnavailable') }}</span>
        </div>

        <button
          class="location-map-card__expand"
          :aria-label="t('dashboard.openFullMap')"
          :title="t('dashboard.openFullMap')"
          @click.stop="openFullMap"
        >
          <font-awesome-icon icon="map" />
        </button>
      </div>
    </div>
  </CardInfoWrap>
</template>
