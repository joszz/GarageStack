<script setup lang="ts">
import '@/assets/dashboard.css'
import { onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { LMap, LMarker } from '@vue-leaflet/vue-leaflet'
import { L, boundsAroundCar, fitToBounds, fitToPoints, type LeafletMap } from '@/utils/leaflet'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import { useCarLocation, useParkedAddress } from '@/composables/useCarLocation'
import CardInfoWrap from './CardInfoWrap.vue'
import { buildCarMarkerIcon, moveCarMarker } from '@/utils/mapCarIcon'
import { latLngsOf } from '@/utils/geo'
import LocationMapActions from './LocationMapActions.vue'

const { t } = useI18n()
const car = useCarLocation()
const { status, location, hasLocation, center, activeTrip, openFullMap } = car
const { address, addressPending } = useParkedAddress(car)

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
const { mapInstance, bindMapReady, unbindMap } = useLeafletMap(mapWrapperRef)
// A preview that cannot be panned or zoomed: raster tiles show the same streets without pulling
// the vector renderer into the dashboard, the page most visits start on.
useBasemap(mapInstance, { vector: false })
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
    const coords = latLngsOf(trip.points)
    routeLine = L.polyline(coords, { color: '#3b82f6', weight: 4, opacity: 0.8 }).addTo(map)
  }
  carMarker = L.marker(center.value, {
    icon: buildCarMarkerIcon(status.value?.heading ?? 0),
  }).addTo(map)
}

// While a trip is in progress, fit the view to the entire route travelled so far with the car
// kept at the centre (boundsAroundCar). Falls back to centering on the car when parked, and for a
// trip without fixes: the copy saved for offline use keeps none, and framing the car alone zoomed
// in past the tiles the card had shown (and kept).
function updateMapView() {
  const map = mapInstance.value
  if (!map) return
  const trip = activeTrip.value?.points.length ? activeTrip.value : null
  if (trip && hasLocation.value) {
    fitToBounds(map, boundsAroundCar(center.value, trip.points), 24)
  } else if (trip && trip.points.length > 0) {
    fitToPoints(map, latLngsOf(trip.points), 24)
  } else if (hasLocation.value) {
    map.setView(center.value, 14, { animate: false })
  }
}

function onMapReady(map: LeafletMap) {
  if (!hasLocation.value) return
  bindMapReady(map, () => {
    buildActiveTripLayers()
    updateMapView()
  })
}

watch(hasLocation, (available) => {
  if (!available) {
    clearActiveTripLayers()
    unbindMap()
  }
})

// Keep the preview following the car (or the whole in-progress trip) as new status updates
// arrive, and keep the live car marker following position/heading without a full layer rebuild.
watch(status, (s) => {
  const point = location.value
  if (!mapInstance.value || !point) return
  if (carMarker) moveCarMarker(carMarker, [point.latitude, point.longitude], s?.heading ?? null)
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
</script>

<template>
  <CardInfoWrap :title="t('vehicle.location')" :description="t('dashboard.cardDesc.location')">
    <div class="location-map-card">
      <p class="card-title">
        <font-awesome-icon icon="location-dot" />
        {{ t('vehicle.location') }}
      </p>

      <!-- Held to select and copy, so not a long press into the layout editor (useLongPress). -->
      <p v-if="address" class="location-map-card__address long-press-exempt" :title="address">
        {{ address }}
      </p>
      <p v-else-if="addressPending" class="location-map-card__address">
        <span
          class="skeleton skeleton--text skeleton--text-md"
          role="status"
          :aria-label="t('vehicle.locatingAddress')"
        />
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
          <LMarker v-if="!activeTrip" :lat-lng="center" />
        </LMap>
        <div v-else class="location-map-card__empty">
          <font-awesome-icon icon="location-dot" />
          <span>{{ t('vehicle.locationUnavailable') }}</span>
        </div>

        <LocationMapActions :location="location" @open-full-map="openFullMap" />
      </div>
    </div>
  </CardInfoWrap>
</template>
