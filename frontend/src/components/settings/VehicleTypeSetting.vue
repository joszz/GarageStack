<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore, type VehicleTypeOverride } from '@/stores/settingsUi'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useVehicleStore } from '@/stores/vehicle'
import { useConnection } from '@/composables/useConnection'
import { useErrorMessage } from '@/composables/useErrorMessage'
import SettingsSelect, { type SettingsSelectOption } from '../SettingsSelect.vue'

// What the gateway says the car is, and the override for when it gets that wrong. Kept with the
// car on the server, so the Worker's alerts follow it too and each car has its own.
const { t } = useI18n()
const settings = useUiSettingsStore()
const dashboardSettings = useDashboardSettingsStore()
const vehicleStore = useVehicleStore()
const connection = useConnection()
const errorMessage = useErrorMessage()

const saving = ref<VehicleTypeOverride | null>(null)
const error = ref<string | null>(null)

// The type being saved until the save ends, then the one in effect: the car's own, or one this
// account saved in its browser settings before the type moved to the car.
const chosen = computed(
  (): VehicleTypeOverride => saving.value ?? vehicleStore.vehicleTypeOverride ?? 'auto',
)

// The cards that depend on the drivetrain take the chosen type's defaults. Only here, where the
// driver makes the choice: the same choice arriving from another device comes with the cards
// that already follow it, and whatever was changed there since must stay.
async function chooseType(type: VehicleTypeOverride) {
  const vin = vehicleStore.activeVin
  if (!vin || saving.value !== null) return
  error.value = null
  saving.value = type
  try {
    const vehicleType = type === 'auto' || type === 'unknown' ? null : type
    // A plain hybrid keeps no battery size of its own, so one set for a plug-in goes with it.
    await vehicleStore.saveSettings(
      vin,
      vehicleType === 'hev' ? { vehicleType, hvBatteryCapacityKwh: null } : { vehicleType },
    )
    // The car holds the choice now; the browser's older one would only shadow a later 'auto'.
    settings.vehicleTypeOverride = 'auto'
    dashboardSettings.applyTypeDefaults(vehicleStore.effectiveVehicleType)
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    saving.value = null
  }
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
    :model-value="chosen"
    :label="t('settings.vehicleType.title')"
    :desc="desc"
    :options="typeOptions"
    :disabled="saving !== null || !vehicleStore.activeVin || !connection.reachable"
    @update:model-value="chooseType"
  >
    <template #note>
      <p v-if="error" class="text-danger text-sm" role="alert">{{ error }}</p>
    </template>
  </SettingsSelect>
</template>
