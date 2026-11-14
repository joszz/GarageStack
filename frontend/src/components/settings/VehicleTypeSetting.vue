<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore, type VehicleTypeOverride } from '@/stores/settingsUi'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useVehicleStore } from '@/stores/vehicle'
import SettingsSelect, { type SettingsSelectOption } from '../SettingsSelect.vue'

// What the gateway says the car is, and the override for when it gets that wrong.
const { t } = useI18n()
const settings = useUiSettingsStore()
const dashboardSettings = useDashboardSettingsStore()
const vehicleStore = useVehicleStore()

// The cards that depend on the drivetrain take the chosen type's defaults. Only here, where the
// driver makes the choice: the same choice arriving from another device comes with the cards
// that already follow it, and whatever was changed there since must stay.
function chooseType(type: VehicleTypeOverride) {
  settings.vehicleTypeOverride = type
  dashboardSettings.applyTypeDefaults(vehicleStore.effectiveVehicleType)
}

// What the gateway reported, with the raw series it came from, so a wrong guess is plain to see.
const desc = computed(() => {
  const type = vehicleStore.detectedVehicleType
  if (type === 'unknown') return t('settings.vehicleType.notDetected')
  const series = vehicleStore.vehicleConfig['hw_version']
  const detected = series ? `${type.toUpperCase()} (${series})` : type.toUpperCase()
  return t('settings.vehicleType.desc', { type: detected })
})

// Computed so the labels follow a language switch made in this same dialog.
const typeOptions = computed((): SettingsSelectOption<VehicleTypeOverride>[] => [
  { value: 'auto', label: t('settings.vehicleType.auto') },
  { value: 'hev', label: t('settings.vehicleType.hev') },
  { value: 'phev', label: t('settings.vehicleType.phev') },
  { value: 'bev', label: t('settings.vehicleType.bev') },
])
</script>

<template>
  <SettingsSelect
    id="settings-vehicle-type"
    :model-value="settings.vehicleTypeOverride"
    :label="t('settings.vehicleType.title')"
    :desc="desc"
    :options="typeOptions"
    @update:model-value="chooseType"
  />
</template>
