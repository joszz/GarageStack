<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useUnits } from '@/composables/useUnits'
import { useErrorMessage } from '@/composables/useErrorMessage'
import { reloadTyrePressureThresholds } from '@/composables/useTyrePressureThresholds'
import { formatNumber } from '@/utils/format'
import type { VehicleSettingsValues } from '@/services/vehicleApi'
import { numberFields, type NumberField } from './vehicleSettingsFields'
import SettingsToggle from '../SettingsToggle.vue'

// The car's own battery size, tyre bands and alert levels, kept on the server so the Worker's
// alerts follow them too. An empty field takes the deployment's default, shown as its placeholder.
const { t } = useI18n()
const errorMessage = useErrorMessage()
const units = useUnits()
const vehicleStore = useVehicleStore()

const busy = ref(false)
const error = ref<string | null>(null)
const done = ref(false)

// What each field holds as typed: a number, or '' while empty. v-model gives a number input's
// value as a number once it reads as one.
const draft = reactive<Record<NumberField, number | string>>(
  Object.fromEntries(numberFields.map((f) => [f.key, ''])) as Record<NumberField, number | string>,
)

const carName = computed(() => {
  const car = vehicleStore.activeVehicle
  return car?.model ?? car?.series ?? car?.vin ?? ''
})

// A plain hybrid has no battery setting: its buffer is not the pack the kWh figures are about.
const visibleFields = computed(() =>
  numberFields.filter((f) => f.appliesTo(vehicleStore.effectiveVehicleType)),
)

function isPressure(key: NumberField): boolean {
  return numberFields.find((f) => f.key === key)?.quantity === 'pressure'
}

// One decimal more than a gauge shows, so a value in psi goes back to bar without drifting.
function pressureDecimals(): number {
  return units.value.decimals('pressure') + 1
}

function toShown(key: NumberField, value: number | null): number | string {
  if (value === null) return ''
  if (!isPressure(key)) return value
  return Number(units.value.convert('pressure', value).toFixed(pressureDecimals()))
}

function toApi(key: NumberField, shown: number): number {
  return isPressure(key) ? Number(units.value.toMetric('pressure', shown).toFixed(3)) : shown
}

function placeholder(key: NumberField): string {
  const value = vehicleStore.vehicleSettings?.defaults[key] ?? null
  if (value === null) return t('settings.vehicleSettings.noDefault')
  if (isPressure(key)) return units.value.measure('pressure', value)?.value ?? ''
  return formatNumber(value, numberFields.find((f) => f.key === key)?.decimals ?? 1)
}

function label(key: NumberField): string {
  return t(`settings.vehicleSettings.fields.${key}`, { unit: units.value.symbol('pressure') })
}

function reset() {
  const values = vehicleStore.vehicleSettings?.values
  for (const field of numberFields)
    draft[field.key] = toShown(field.key, values?.[field.key] ?? null)
}

// Filled from what is saved, again whenever that or the pressure unit changes.
watch(() => [vehicleStore.vehicleSettings, units.value.preferences.pressure] as const, reset, {
  immediate: true,
})

watch(
  () => vehicleStore.activeVin,
  async (vin) => {
    if (!vin) return
    try {
      await vehicleStore.fetchSettings(vin)
    } catch (e) {
      error.value = errorMessage(e)
    }
  },
  { immediate: true },
)

// Only what was changed is sent, so a value shown rounded in psi is not saved back rounded.
const changes = computed((): Partial<VehicleSettingsValues> => {
  const saved = vehicleStore.vehicleSettings?.values
  const out: Partial<VehicleSettingsValues> = {}
  for (const field of visibleFields.value) {
    const typed = draft[field.key]
    if (typed === toShown(field.key, saved?.[field.key] ?? null)) continue
    if (typed === '') out[field.key] = null
    else if (Number.isFinite(Number(typed))) out[field.key] = toApi(field.key, Number(typed))
  }
  return out
})

const changed = computed(() => Object.keys(changes.value).length > 0)

async function save() {
  const vin = vehicleStore.activeVin
  if (!vin || busy.value || !changed.value) return
  error.value = null
  done.value = false
  busy.value = true
  try {
    await vehicleStore.saveSettings(vin, changes.value)
    await reloadTyrePressureThresholds(vin)
    done.value = true
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <SettingsToggle
    class="settings-toggle--stacked"
    :label="t('settings.vehicleSettings.title', { car: carName })"
    :desc="t('settings.vehicleSettings.desc')"
  >
    <template #control>
      <form class="vehicle-settings-form" novalidate @submit.prevent="save">
        <div v-for="field in visibleFields" :key="field.key" class="form-group">
          <label :for="`vehicle-setting-${field.key}`">{{ label(field.key) }}</label>
          <input
            :id="`vehicle-setting-${field.key}`"
            v-model="draft[field.key]"
            class="form-control"
            type="number"
            inputmode="decimal"
            step="any"
            :min="field.quantity === 'pressure' ? undefined : field.min"
            :max="field.quantity === 'pressure' ? undefined : field.max"
            :placeholder="placeholder(field.key)"
            :data-field="field.key"
          />
        </div>
        <div class="vehicle-settings-form__actions">
          <button type="submit" class="btn btn-primary" :disabled="busy || !changed">
            {{ t('settings.vehicleSettings.save') }}
          </button>
          <button
            type="button"
            class="btn btn-outline-secondary"
            :disabled="busy || !changed"
            @click="reset"
          >
            {{ t('settings.vehicleSettings.undo') }}
          </button>
        </div>
      </form>
    </template>
    <template #note>
      <p v-if="error" class="text-danger text-sm" role="alert">{{ error }}</p>
      <p v-else-if="done && !changed" class="text-muted text-sm" role="status">
        {{ t('settings.vehicleSettings.saved') }}
      </p>
    </template>
  </SettingsToggle>
</template>
