<script setup lang="ts">
import { defineAsyncComponent, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useCommutePlacesStore } from '@/stores/commutePlaces'
import { COMMUTE_PLACE_ICONS } from '@/utils/commutePlaces'
import SettingsToggle from '../SettingsToggle.vue'

// The dialog brings a map with it, which the settings need only once someone opens it.
const CommutePlacesModal = defineAsyncComponent(() => import('../CommutePlacesModal.vue'))

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const places = useCommutePlacesStore()

const opened = ref(false)
const open = ref(false)

function show() {
  const vin = vehicleStore.activeVin
  if (!vin) return
  // The list may still be another car's, or from before a change on another device.
  void places.fetchPlaces(vin)
  opened.value = true
  open.value = true
}
</script>

<template>
  <SettingsToggle
    class="settings-toggle--select settings-toggle--action"
    :label="t('settings.commutePlaces.title')"
    :desc="t('settings.commutePlaces.desc')"
  >
    <template #control>
      <button
        type="button"
        class="btn btn-outline-secondary"
        :disabled="!vehicleStore.activeVin"
        @click="show"
      >
        <font-awesome-icon :icon="COMMUTE_PLACE_ICONS.home" />
        {{ t('settings.commutePlaces.edit') }}
      </button>
    </template>
  </SettingsToggle>
  <CommutePlacesModal
    v-if="opened"
    :open="open"
    :vin="vehicleStore.activeVin"
    :pick="null"
    :fallback-center="vehicleStore.currentPosition"
    @close="open = false"
  />
</template>
