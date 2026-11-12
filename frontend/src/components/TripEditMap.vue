<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { LCircleMarker, LIcon, LMap, LMarker, LPolyline } from '@vue-leaflet/vue-leaflet'
import type { DragEndEvent, LeafletMouseEvent, Marker } from 'leaflet'
import type { TripPoint } from '@/services/vehicleApi'
import type { TripStop } from '@/services/tripLogApi'
import type { TripEditMode } from '@/composables/useTripEditor'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import { DEFAULT_MAP_CENTER, fitToPoints, withNearby, type LeafletMap } from '@/utils/leaflet'
import { latLngsOf, type LatLng } from '@/utils/geo'
import { addedStretches, phoneStretches } from '@/utils/tripSources'
import PhoneSourceKey from './PhoneSourceKey.vue'
import { sameRoute } from '@/utils/tripEdit'

const props = defineProps<{
  points: readonly TripPoint[]
  /** The route as recorded, drawn faintly under the edited one while they differ. */
  recorded: readonly TripPoint[]
  mode: TripEditMode
  /** The point picked for removal, as an index into `points`. */
  selected: number | null
  previousStop: TripStop | null
  nextStop: TripStop | null
}>()

const emit = defineEmits<{
  /** The map was tapped there: what that does depends on the mode. */
  (e: 'place', latitude: number, longitude: number): void
  (e: 'moveStart', latitude: number, longitude: number): void
  (e: 'moveEnd', latitude: number, longitude: number): void
  (e: 'selectPoint', index: number): void
}>()

const { t } = useI18n()
const wrapper = useTemplateRef<HTMLElement>('wrapper')
const { mapInstance, bindMapReady } = useLeafletMap(wrapper)
useBasemap(mapInstance)

const line = computed(() => latLngsOf(props.points))
const recordedLine = computed(() =>
  sameRoute(props.points, props.recorded) ? null : latLngsOf(props.recorded),
)
const dashes = computed(() => addedStretches(props.points))
const phoneLines = computed(() => phoneStretches(props.points))
const start = computed<LatLng | null>(() => line.value[0] ?? null)
// Fixed once the map is up: moving the start must not pan the map out from under the finger.
const initialCenter: LatLng = start.value ?? DEFAULT_MAP_CENTER
const end = computed<LatLng | null>(() => line.value[line.value.length - 1] ?? null)

// The points between the ends, which points mode picks from; the ends are moved, not removed.
const between = computed(() =>
  props.mode === 'points'
    ? line.value.slice(1, -1).map((latLng, i) => ({ latLng, index: i + 1 }))
    : [],
)

const stops = computed(() =>
  [props.previousStop, props.nextStop]
    .filter((s): s is TripStop => s !== null)
    .map((s): LatLng => [s.latitude, s.longitude]),
)

// The end a tap on the map moves is marked, so it is clear what the tap will do.
function markerClass(which: 'start' | 'end'): string {
  const active = props.mode === which ? ' trip-edit-marker--active' : ''
  return `trip-edit-marker trip-edit-marker--${which}${active}`
}

// Big enough to hit with a thumb, whatever the dot inside looks like.
const MARKER_SIZE: [number, number] = [44, 44]
const MARKER_ANCHOR: [number, number] = [22, 22]

// Framed on the route; a parking spot either side joins it only when it is close by, since one
// 40 km away would leave the route a few pixels long. The map does not follow later edits, so a
// marker never moves out from under the finger.
function onMapReady(map: LeafletMap) {
  bindMapReady(map, (ready) =>
    fitToPoints(ready, withNearby([...latLngsOf(props.recorded), ...line.value], stops.value), 48),
  )
}

function onMapClick(event: LeafletMouseEvent) {
  emit('place', event.latlng.lat, event.latlng.lng)
}

function onDragEnd(which: 'start' | 'end', event: DragEndEvent) {
  const { lat, lng } = (event.target as Marker).getLatLng()
  if (which === 'start') emit('moveStart', lat, lng)
  else emit('moveEnd', lat, lng)
}
</script>

<template>
  <div ref="wrapper" class="map-wrapper trip-edit-map">
    <LMap
      :zoom="13"
      :center="initialCenter"
      class="map-canvas"
      @ready="onMapReady"
      @click="onMapClick"
    >
      <LPolyline
        v-if="recordedLine"
        :lat-lngs="recordedLine"
        class-name="trip-edit-line trip-edit-line--recorded"
        :weight="4"
        dash-array="4 8"
        :interactive="false"
      />
      <LPolyline :lat-lngs="line" class-name="trip-edit-line" :weight="5" :interactive="false" />
      <LPolyline
        v-for="(run, i) in dashes"
        :key="`dash-${i}`"
        :lat-lngs="run"
        class-name="trip-added-line"
        :weight="5"
        dash-array="6 8"
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
      <LCircleMarker
        v-for="stop in stops"
        :key="`stop-${stop[0]}-${stop[1]}`"
        :lat-lng="stop"
        :radius="6"
        class-name="trip-edit-stop"
        :interactive="false"
      />
      <LCircleMarker
        v-for="point in between"
        :key="`point-${point.index}-${point.index === selected}`"
        :lat-lng="point.latLng"
        :radius="point.index === selected ? 11 : 8"
        :class-name="
          point.index === selected ? 'trip-edit-point trip-edit-point--selected' : 'trip-edit-point'
        "
        :weight="3"
        :bubbling-mouse-events="false"
        @click="emit('selectPoint', point.index)"
      />
      <LMarker
        v-if="start"
        :key="markerClass('start')"
        :lat-lng="start"
        :title="t('tripEdit.markerStart')"
        draggable
        @dragend="onDragEnd('start', $event)"
      >
        <LIcon
          :icon-size="MARKER_SIZE"
          :icon-anchor="MARKER_ANCHOR"
          :class-name="markerClass('start')"
        >
          <span class="trip-edit-marker__dot" />
        </LIcon>
      </LMarker>
      <LMarker
        v-if="end"
        :key="markerClass('end')"
        :lat-lng="end"
        :title="t('tripEdit.markerEnd')"
        draggable
        @dragend="onDragEnd('end', $event)"
      >
        <LIcon
          :icon-size="MARKER_SIZE"
          :icon-anchor="MARKER_ANCHOR"
          :class-name="markerClass('end')"
        >
          <span class="trip-edit-marker__dot" />
        </LIcon>
      </LMarker>
    </LMap>
  </div>
  <PhoneSourceKey v-if="phoneLines.length" />
</template>
