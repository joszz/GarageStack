<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useMapSettingsStore } from '@/stores/settingsMap'
import ToolbarPanel from '../ToolbarPanel.vue'
import SettingsToggle from '../SettingsToggle.vue'

// What the map draws at all: the ways of showing trips, and the POI layers this car has a use for.
const props = defineProps<{
  carTakesFuel: boolean
  carTakesCharge: boolean
  /** False when the deployment does not serve the speed camera layer. */
  speedCamerasAvailable: boolean
}>()

const { t } = useI18n()
const {
  heatmapEnabled,
  routeOutlineEnabled,
  snapToRoadsEnabled,
  speedOverlayEnabled,
  speedLimitOverlayEnabled,
  serviceAreasEnabled,
  speedCamerasEnabled,
  fuelStationsEnabled,
  chargingStationsEnabled,
} = storeToRefs(useMapSettingsStore())

// The badge on the panel's button: the layers that are on and would show.
const activeCount = computed(() => {
  const active = [
    heatmapEnabled.value,
    routeOutlineEnabled.value,
    snapToRoadsEnabled.value,
    speedOverlayEnabled.value,
    // Hidden without snapping, and inert too: the limits arrive with the snapped line.
    snapToRoadsEnabled.value && speedLimitOverlayEnabled.value,
    serviceAreasEnabled.value,
    props.speedCamerasAvailable && speedCamerasEnabled.value,
    props.carTakesFuel && fuelStationsEnabled.value,
    props.carTakesCharge && chargingStationsEnabled.value,
  ]
  return active.filter(Boolean).length
})
</script>

<template>
  <ToolbarPanel :title="t('common.layers')" icon="layer-group" :count="activeCount">
    <SettingsToggle
      v-model="heatmapEnabled"
      :label="t('trips.heatmap')"
      :desc="t('trips.heatmapDesc')"
      icon="fire"
    />
    <SettingsToggle
      v-model="routeOutlineEnabled"
      :label="t('trips.routeOutline')"
      :desc="t('trips.routeOutlineDesc')"
      icon="route"
    />
    <SettingsToggle
      v-model="snapToRoadsEnabled"
      :label="t('trips.snapToRoads')"
      :desc="t('trips.snapToRoadsDesc')"
      icon="road-circle-check"
    />
    <SettingsToggle
      v-model="speedOverlayEnabled"
      :label="t('trips.speedOverlay')"
      :desc="t('trips.speedOverlayDesc')"
      icon="gauge"
    />
    <!-- The limits ride along with the snapped line, so without snapping there is nothing
         to colour the trip against. -->
    <SettingsToggle
      v-if="snapToRoadsEnabled"
      v-model="speedLimitOverlayEnabled"
      :label="t('trips.speedLimitOverlay')"
      :desc="t('trips.speedLimitOverlayDesc')"
      icon="gauge-high"
    />
    <SettingsToggle
      v-model="serviceAreasEnabled"
      :label="t('trips.serviceAreas')"
      :desc="t('trips.serviceAreasDesc')"
      icon="road"
    />
    <!-- Left out entirely where the deployment does not serve the layer, which is how a
         jurisdiction that restricts flagging camera positions switches it off. -->
    <SettingsToggle
      v-if="speedCamerasAvailable"
      v-model="speedCamerasEnabled"
      :label="t('trips.speedCameras')"
      :desc="t('trips.speedCamerasDesc')"
      icon="camera"
    />
    <SettingsToggle
      v-if="carTakesFuel"
      v-model="fuelStationsEnabled"
      :label="t('trips.fuelStations')"
      :desc="t('trips.fuelStationsDesc')"
      icon="gas-pump"
    />
    <SettingsToggle
      v-if="carTakesCharge"
      v-model="chargingStationsEnabled"
      :label="t('trips.chargingStations')"
      :desc="t('trips.chargingStationsDesc')"
      icon="bolt"
    />
  </ToolbarPanel>
</template>
