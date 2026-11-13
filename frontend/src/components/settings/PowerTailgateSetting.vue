<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useErrorMessage } from '@/composables/useErrorMessage'
import SettingsToggle from '../SettingsToggle.vue'

// Kept on the vehicle rather than in the browser's settings: the API refuses the tailgate
// command for a car without it, whichever device sends it.
const { t } = useI18n()
const errorMessage = useErrorMessage()
const vehicleStore = useVehicleStore()

// The value being saved, shown until the save ends: then the switch shows what the vehicle holds,
// which flips it back by itself when saving failed.
const saving = ref<boolean | null>(null)
const error = ref<string | null>(null)

const enabled = computed({
  get: () => saving.value ?? vehicleStore.activeVehicle?.hasPowerTailgate ?? false,
  set: (value: boolean) => void save(value),
})

async function save(value: boolean) {
  const vin = vehicleStore.activeVin
  if (!vin || saving.value !== null) return
  error.value = null
  saving.value = value
  try {
    await vehicleStore.setPowerTailgate(vin, value)
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    saving.value = null
  }
}
</script>

<template>
  <div class="settings-toggles">
    <SettingsToggle
      v-model="enabled"
      input-id="settings-power-tailgate"
      icon="car-rear"
      :label="t('settings.powerTailgate.label')"
      :desc="t('settings.powerTailgate.desc')"
      :disabled="saving !== null || !vehicleStore.activeVin"
    />
    <p v-if="error" class="text-danger text-sm" role="alert">{{ error }}</p>
  </div>
</template>
