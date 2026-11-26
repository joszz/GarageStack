<script setup lang="ts">
import { computed, ref, useId } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useMapSettingsStore } from '@/stores/settingsMap'
import ToolbarPanel from '../ToolbarPanel.vue'
import SettingsToggle from '../SettingsToggle.vue'
import SettingsTabs from '../SettingsTabs.vue'

// What the map draws at all: the ways of showing trips, and the POI layers this car has a use for.
const props = defineProps<{
  carTakesFuel: boolean
  carTakesCharge: boolean
  /** False when the deployment does not serve the speed camera layer. */
  speedCamerasAvailable: boolean
  /** True once a phone (Dawarich or the installed app) added points to one of the trips. */
  phoneGpsAvailable: boolean
}>()

const { t } = useI18n()
const id = useId()

// Split by what a layer draws on: every trip shown, the one trip selected (snapping and the speed
// colours, which only apply to a single trip and depend on each other), or places to go.
type LayerTab = 'routes' | 'trip' | 'places'
const activeTab = ref<LayerTab>('routes')
const tabs = computed(() => [
  {
    id: 'routes' as const,
    icon: 'map',
    label: t('trips.layerTabs.routes'),
    shortLabel: t('trips.layerTabs.routesShort'),
  },
  {
    id: 'trip' as const,
    icon: 'route',
    label: t('trips.layerTabs.trip'),
    shortLabel: t('trips.layerTabs.tripShort'),
  },
  { id: 'places' as const, icon: 'location-dot', label: t('trips.layerTabs.places') },
])
const {
  heatmapEnabled,
  routeOutlineEnabled,
  snapToRoadsEnabled,
  speedOverlayEnabled,
  speedLimitOverlayEnabled,
  serviceAreasEnabled,
  speedCamerasEnabled,
  parkingEnabled,
  carWashesEnabled,
  airPumpsEnabled,
  garagesEnabled,
  fuelStationsEnabled,
  chargingStationsEnabled,
  phoneGpsEnabled,
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
    parkingEnabled.value,
    carWashesEnabled.value,
    airPumpsEnabled.value,
    garagesEnabled.value,
    props.carTakesFuel && fuelStationsEnabled.value,
    props.carTakesCharge && chargingStationsEnabled.value,
    props.phoneGpsAvailable && phoneGpsEnabled.value,
  ]
  return active.filter(Boolean).length
})
</script>

<template>
  <ToolbarPanel
    :title="t('common.layers')"
    icon="layer-group"
    variant="map-control"
    :count="activeCount"
  >
    <template #navigation>
      <SettingsTabs
        v-model="activeTab"
        :tabs="tabs"
        :label="t('trips.layerTabs.label')"
        :id-prefix="id"
      />
    </template>

    <!-- Kept mounted between tabs, like the settings dialog's panels, so a switch keeps its
         focus while the rows around it come and go. -->
    <div
      :id="`${id}-panel-routes`"
      role="tabpanel"
      :aria-labelledby="`${id}-tab-routes`"
      :hidden="activeTab !== 'routes'"
      tabindex="0"
    >
      <div class="settings-toggles">
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
        <!-- Only once a phone has added points to a trip: without a location service there is
             nothing to leave out. -->
        <SettingsToggle
          v-if="phoneGpsAvailable"
          v-model="phoneGpsEnabled"
          :label="t('trips.phoneGps')"
          :desc="t('trips.phoneGpsDesc')"
          icon="mobile-screen"
        />
      </div>
    </div>

    <div
      :id="`${id}-panel-trip`"
      role="tabpanel"
      :aria-labelledby="`${id}-tab-trip`"
      :hidden="activeTab !== 'trip'"
      tabindex="0"
    >
      <div class="settings-toggles">
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
      </div>
    </div>

    <div
      :id="`${id}-panel-places`"
      role="tabpanel"
      :aria-labelledby="`${id}-tab-places`"
      :hidden="activeTab !== 'places'"
      tabindex="0"
    >
      <div class="settings-toggles">
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
          v-model="parkingEnabled"
          :label="t('poi.parking.layer')"
          :desc="t('poi.parking.layerDesc')"
          icon="square-parking"
        />
        <SettingsToggle
          v-model="carWashesEnabled"
          :label="t('poi.carWash.layer')"
          :desc="t('poi.carWash.layerDesc')"
          icon="car-side"
        />
        <SettingsToggle
          v-model="airPumpsEnabled"
          :label="t('poi.air.layer')"
          :desc="t('poi.air.layerDesc')"
          icon="wind"
        />
        <SettingsToggle
          v-model="garagesEnabled"
          :label="t('poi.repair.layer')"
          :desc="t('poi.repair.layerDesc')"
          icon="screwdriver-wrench"
        />
      </div>
    </div>
  </ToolbarPanel>
</template>
