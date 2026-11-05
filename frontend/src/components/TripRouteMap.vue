<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { LCircleMarker, LMap, LPolyline } from '@vue-leaflet/vue-leaflet'
import type { TripPoint } from '@/services/vehicleApi'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import { fitToPoints, type LeafletMap } from '@/utils/leaflet'
import { latLngsOf, type LatLng } from '@/utils/geo'
import { speedSegments, speedToColor } from '@/utils/speedColors'

const props = defineProps<{
  /** The route's fixes; null while they load or when they could not be. */
  points: readonly TripPoint[] | null
  loading: boolean
  /** Where the trip started and ended, for a straight line when the fixes are not to be had. */
  start: LatLng
  end: LatLng
  tripId: number
  startedAt: string
  /** A hidden trip is left off the map, so there is nothing there to open. */
  linkToMap: boolean
}>()

const { t } = useI18n()

// A preview that cannot be panned or zoomed, as the dashboard's location card.
const mapOptions = {
  zoomControl: false,
  dragging: false,
  scrollWheelZoom: false,
  doubleClickZoom: false,
  touchZoom: false,
  boxZoom: false,
  keyboard: false,
  attributionControl: true,
}

const wrapper = ref<HTMLElement | null>(null)
const { mapInstance, bindMapReady } = useLeafletMap(wrapper)
// Raster tiles: the same streets without pulling the vector renderer into the trip log.
useBasemap(mapInstance, { vector: false })

// The fixes the car reported no speed for are drawn in the line colour of the map's trips.
const NO_SPEED_COLOR = '#3b82f6'

const segments = computed(() =>
  props.points && props.points.length >= 2
    ? speedSegments({ points: [...props.points] }, null).map((segment) => ({
        coordinates: segment.coordinates,
        color: speedToColor(segment.speed, NO_SPEED_COLOR),
      }))
    : [],
)

// Without fixes, once they stopped loading, the ends joined by a straight line still say where
// the trip went.
const straightLine = computed<LatLng[] | null>(() =>
  !props.loading && segments.value.length === 0 ? [props.start, props.end] : null,
)

const framed = computed<LatLng[]>(() =>
  props.points && props.points.length > 0 ? latLngsOf(props.points) : [props.start, props.end],
)

function frame(map: LeafletMap) {
  fitToPoints(map, framed.value, 24)
}

function onMapReady(map: LeafletMap) {
  bindMapReady(map, frame)
}

watch(framed, () => {
  if (mapInstance.value) frame(mapInstance.value)
})
</script>

<template>
  <div ref="wrapper" class="trip-route-map" :aria-label="t('tripLog.detail.routePreview')">
    <LMap
      :zoom="13"
      :center="start"
      :options="mapOptions"
      class="trip-route-map__canvas"
      @ready="onMapReady"
    >
      <LPolyline
        v-for="(segment, i) in segments"
        :key="`segment-${i}`"
        :lat-lngs="segment.coordinates"
        :color="segment.color"
        :weight="5"
        :opacity="0.9"
        :interactive="false"
      />
      <LPolyline
        v-if="straightLine"
        :lat-lngs="straightLine"
        class-name="trip-route-map__straight"
        :weight="3"
        dash-array="6 8"
        :interactive="false"
      />
      <LCircleMarker
        :lat-lng="start"
        :radius="6"
        class-name="trip-route-map__end trip-route-map__end--start"
        :interactive="false"
      />
      <LCircleMarker
        :lat-lng="end"
        :radius="6"
        class-name="trip-route-map__end trip-route-map__end--finish"
        :interactive="false"
      />
    </LMap>

    <span
      v-if="loading"
      class="trip-route-map__loading skeleton"
      role="status"
      :aria-label="t('tripEdit.loading')"
    />

    <RouterLink
      v-if="linkToMap"
      class="trip-route-map__open"
      :to="{ name: 'map', query: { trip: tripId, at: startedAt } }"
      :aria-label="t('tripLog.showOnMap')"
      :title="t('tripLog.showOnMap')"
    >
      <font-awesome-icon icon="map" />
    </RouterLink>
  </div>
</template>
