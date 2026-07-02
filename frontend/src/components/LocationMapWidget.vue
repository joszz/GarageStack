<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { LMap, LTileLayer, LMarker } from '@vue-leaflet/vue-leaflet'
import type { Map as LeafletMap } from 'leaflet'
import { useVehicleStore } from '@/stores/vehicle'
import CardInfoWrap from './CardInfoWrap.vue'

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
const mapInstance = ref<LeafletMap | null>(null)
let resizeObserver: ResizeObserver | null = null

function onMapReady(map: LeafletMap) {
  mapInstance.value = map
  if (mapWrapperRef.value) {
    resizeObserver = new ResizeObserver(() => map.invalidateSize())
    resizeObserver.observe(mapWrapperRef.value)
  }
  nextTick(() => {
    requestAnimationFrame(() => {
      map.invalidateSize()
      if (hasLocation.value) map.setView(center.value, 14, { animate: false })
    })
  })
}

// Keep the preview centred on the car as new status updates arrive.
watch(status, (s) => {
  if (mapInstance.value && s?.latitude != null && s?.longitude != null) {
    mapInstance.value.setView([s.latitude, s.longitude], 14, { animate: false })
  }
})

onUnmounted(() => resizeObserver?.disconnect())

function openFullMap() {
  router.push({ name: 'map' })
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
          <LMarker :lat-lng="center" />
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
