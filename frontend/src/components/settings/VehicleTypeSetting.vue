<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Multiselect from '@vueform/multiselect'
import { useUiSettingsStore, type VehicleTypeOverride } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'

// What the gateway says the car is, and the override for when it gets that wrong.
const { t } = useI18n()
const settings = useUiSettingsStore()
const vehicleStore = useVehicleStore()

const detectedLabel = computed(() => {
  const type = vehicleStore.detectedVehicleType
  if (type === 'unknown') return null
  return type.toUpperCase()
})

const hwVersion = computed(() => vehicleStore.vehicleConfig['hw_version'] ?? null)

// Computed so the labels follow a language switch made in this same dialog.
const typeOptions = computed((): { value: VehicleTypeOverride; label: string }[] => [
  { value: 'auto', label: t('settings.vehicleType.auto') },
  { value: 'hev', label: t('settings.vehicleType.hev') },
  { value: 'phev', label: t('settings.vehicleType.phev') },
  { value: 'bev', label: t('settings.vehicleType.bev') },
])
</script>

<template>
  <div class="vehicle-type-row">
    <div class="vehicle-type-detected">
      <span class="text-muted">{{ t('settings.vehicleType.detected') }}:</span>
      <span v-if="detectedLabel" class="badge badge-info ms-2">{{ detectedLabel }}</span>
      <span v-if="hwVersion" class="text-muted ms-2 text-xs">({{ hwVersion }})</span>
      <span v-if="!detectedLabel" class="text-muted ms-2">{{
        t('settings.vehicleType.notDetected')
      }}</span>
    </div>
    <div class="vehicle-type-override">
      <label class="text-muted">{{ t('settings.vehicleType.override') }}</label>
      <Multiselect
        v-model="settings.vehicleTypeOverride"
        :options="typeOptions"
        :searchable="false"
        :can-clear="false"
        :can-deselect="false"
        append-to="body"
        class="vehicle-type-select"
      />
    </div>
  </div>
</template>
