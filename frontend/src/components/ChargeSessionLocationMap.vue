<script setup lang="ts">
import { ref } from 'vue'
import { LMap, LMarker } from '@vue-leaflet/vue-leaflet'
import { useLeafletMap } from '@/composables/useLeafletMap'
import { useBasemap } from '@/composables/useBasemap'
import type { LeafletMap } from '@/utils/leaflet'

const props = defineProps<{
  latitude: number
  longitude: number
  label: string
}>()

const wrapper = ref<HTMLElement | null>(null)
const { mapInstance, bindMapReady } = useLeafletMap(wrapper)
useBasemap(mapInstance, { vector: false })

const mapOptions = {
  zoomControl: false,
  dragging: false,
  scrollWheelZoom: false,
  doubleClickZoom: false,
  touchZoom: false,
  boxZoom: false,
  keyboard: false,
}

function onMapReady(map: LeafletMap): void {
  bindMapReady(map, (ready) => {
    ready.setView([props.latitude, props.longitude], 14, { animate: false })
  })
}
</script>

<template>
  <div ref="wrapper" class="charge-history-map" :aria-label="label">
    <LMap
      :zoom="14"
      :center="[props.latitude, props.longitude]"
      :options="mapOptions"
      class="charge-history-map__canvas"
      @ready="onMapReady"
    >
      <LMarker :lat-lng="[props.latitude, props.longitude]" />
    </LMap>
  </div>
</template>
