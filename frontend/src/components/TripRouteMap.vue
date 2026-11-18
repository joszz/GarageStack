<script setup lang="ts">
import '@/assets/tripMarkers.css'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { LMap, LPolyline } from '@vue-leaflet/vue-leaflet'
import type { TripPoint } from '@/services/vehicleApi'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import { fitToPoints, L, type FitPadding, type LeafletMap } from '@/utils/leaflet'
import { latLngsOf, type LatLng } from '@/utils/geo'
import { speedSegments, speedToColor } from '@/utils/speedColors'
import { phoneStretches } from '@/utils/tripSources'
import { tripEndIcon, tripStartIcon } from '@/utils/tripMarkers'
import PhoneSourceKey from './PhoneSourceKey.vue'

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
const phoneLines = computed(() => phoneStretches(props.points ?? []))

// A preview that cannot be panned or zoomed, as the dashboard's location card. It zooms in
// quarter steps, so a trip fills the preview instead of waiting for the next whole zoom level,
// which can leave it at half the size it could be.
const mapOptions = {
  zoomSnap: 0.25,
  zoomControl: false,
  dragging: false,
  scrollWheelZoom: false,
  doubleClickZoom: false,
  touchZoom: false,
  boxZoom: false,
  keyboard: false,
  attributionControl: true,
}

// Where the map opens, before the trip is framed. A copy taken once: the detail hands a new array
// for the start on every render, and vue-leaflet sets the view again whenever the centre it was
// given changes, which put the start back in the middle at zoom 13 and an end out of sight.
const initialCenter: LatLng = [props.start[0], props.start[1]]

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

// The route and both of its ends: the markers stand on the trip's own start and end, which an
// anchored start or a route edited since need not share with the first and last fix.
const framed = computed<LatLng[]>(() => [
  ...(props.points ? latLngsOf(props.points) : []),
  props.start,
  props.end,
])

// Both ends stay in view, markers and all: the flag stands 32 px tall and 20 px wide over the end,
// and the attribution runs along the bottom. The buttons cover only the top right corner, so the
// trip is framed as closely as that allows first, and only when part of it lands under them is it
// framed again with their height kept free along the top.
const TIGHT_PADDING: FitPadding = { topLeft: [24, 40], bottomRight: [32, 28] }
const CLEAR_OF_BUTTONS: FitPadding = { topLeft: [24, 56], bottomRight: [32, 28] }
// How far each thing drawn on a point reaches up and to its right, plus a little air: the flag
// over the end, the pulsing dot on the start, and the 5 px line along the route.
const REACH = {
  end: { right: 24, up: 36 },
  start: { right: 14, up: 14 },
  route: { right: 6, up: 6 },
}

const tools = ref<HTMLElement | null>(null)

function underButtons(map: LeafletMap): boolean {
  const box = tools.value?.getBoundingClientRect()
  const container = map.getContainer().getBoundingClientRect()
  if (!box || box.width === 0) return false
  const left = box.left - container.left
  const bottom = box.bottom - container.top
  const drawn = [
    ...(props.points ? latLngsOf(props.points) : []).map((latLng) => ({ latLng, ...REACH.route })),
    { latLng: props.start, ...REACH.start },
    { latLng: props.end, ...REACH.end },
  ]
  return drawn.some(({ latLng, right, up }) => {
    const point = map.latLngToContainerPoint(latLng)
    return point.x + right >= left && point.y - up <= bottom
  })
}

function frame(map: LeafletMap) {
  fitToPoints(map, framed.value, TIGHT_PADDING)
  if (underButtons(map)) fitToPoints(map, framed.value, CLEAR_OF_BUTTONS)
}

// The panel's width can change after the first frame, as when the speed chart arrives and gives
// it a scrollbar. Leaflet keeps the centre on a resize, which can push an end off the edge, so
// the route is framed again instead.
function onMapReady(map: LeafletMap) {
  bindMapReady(map, (ready) => {
    placeEnds(ready)
    frame(ready)
    ready.on('resize', () => frame(ready))
  })
}

// The pulsing dot and the flag the map page marks a trip's ends with, added the way the map page
// adds them: Leaflet builds their markup from the shared icons rather than a template repeating it.
let ends: { start: L.Marker; end: L.Marker } | null = null

function placeEnds(map: LeafletMap) {
  if (ends) {
    ends.start.setLatLng(props.start)
    ends.end.setLatLng(props.end)
    return
  }
  const options = { interactive: false, keyboard: false }
  ends = {
    start: L.marker(props.start, { ...options, icon: tripStartIcon() }).addTo(map),
    end: L.marker(props.end, { ...options, icon: tripEndIcon() }).addTo(map),
  }
}

watch(framed, () => {
  if (!mapInstance.value) return
  placeEnds(mapInstance.value)
  frame(mapInstance.value)
})
</script>

<template>
  <div ref="wrapper" class="trip-route-map" :aria-label="t('tripLog.detail.routePreview')">
    <LMap
      :zoom="13"
      :center="initialCenter"
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
        v-for="(run, i) in phoneLines"
        :key="`phone-${i}`"
        :lat-lngs="run"
        class-name="trip-phone-line"
        :weight="5"
        dash-array="2 8"
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
    </LMap>

    <span
      v-if="loading"
      class="trip-route-map__loading skeleton"
      role="status"
      :aria-label="t('tripEdit.loading')"
    />

    <div ref="tools" class="trip-route-map__tools">
      <RouterLink
        class="trip-route-map__button"
        :to="{ name: 'tripEdit', params: { id: tripId } }"
        :aria-label="t('tripLog.editRoute')"
        :title="t('tripLog.editRoute')"
      >
        <font-awesome-icon icon="pen-to-square" />
      </RouterLink>
      <RouterLink
        v-if="linkToMap"
        class="trip-route-map__button"
        :to="{ name: 'map', query: { trip: tripId, at: startedAt } }"
        :aria-label="t('tripLog.showOnMap')"
        :title="t('tripLog.showOnMap')"
      >
        <font-awesome-icon icon="map" />
      </RouterLink>
    </div>
  </div>
  <PhoneSourceKey v-if="phoneLines.length" />
</template>
