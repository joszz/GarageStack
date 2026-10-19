<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRdwStore } from '@/stores/rdw'
import { useVehicleStore } from '@/stores/vehicle'
import { LICENSE_PLATE_INPUT_MAX_LENGTH } from '@/services/rdwApi'
import { useErrorMessage } from '@/composables/useErrorMessage'

// The car's Dutch plate, kept on the vehicle (the Worker looks it up) rather than in the
// browser's settings. Saving looks it up at once, so the dashboard card fills straight away.
const { t } = useI18n()
const errorMessage = useErrorMessage()
const rdw = useRdwStore()
const vehicleStore = useVehicleStore()

const saved = computed(() => rdw.status?.licensePlate ?? '')
const plate = ref(saved.value)
const busy = ref(false)
const error = ref<string | null>(null)
const done = ref<string | null>(null)

// Follows a plate saved elsewhere (another tab, a device) until the user starts typing here.
watch(saved, (value, previous) => {
  if (plate.value === previous) plate.value = value
})

// The API keys a plate without its dashes and spaces, as the RDW does.
const normalized = computed(() => plate.value.replace(/[\s-]/g, '').toUpperCase())
const changed = computed(() => normalized.value !== saved.value)

async function save(value: string) {
  const vin = vehicleStore.activeVin
  if (!vin || busy.value) return
  error.value = null
  done.value = null
  busy.value = true
  try {
    await rdw.setLicensePlate(vin, value)
    plate.value = rdw.status?.licensePlate ?? ''
    done.value = plate.value ? t('settings.licensePlate.saved') : t('settings.licensePlate.cleared')
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="form-stack">
    <p class="settings-toggle__desc">{{ t('settings.licensePlate.desc') }}</p>

    <form class="license-plate-form" @submit.prevent="save(plate)">
      <input
        v-model="plate"
        class="form-control license-plate-form__input"
        autocomplete="off"
        autocapitalize="characters"
        spellcheck="false"
        :maxlength="LICENSE_PLATE_INPUT_MAX_LENGTH"
        :placeholder="t('settings.licensePlate.placeholder')"
        :aria-label="t('settings.licensePlate.label')"
      />
      <button type="submit" class="btn btn-primary btn-sm" :disabled="busy || !changed">
        {{ t('settings.licensePlate.save') }}
      </button>
      <button
        v-if="saved"
        type="button"
        class="btn btn-outline-secondary btn-sm"
        :disabled="busy"
        @click="save('')"
      >
        {{ t('settings.licensePlate.clear') }}
      </button>
    </form>

    <p v-if="error" class="text-danger text-sm" role="alert">{{ error }}</p>
    <p v-else-if="done" class="text-muted text-sm" role="status">{{ done }}</p>
  </div>
</template>
