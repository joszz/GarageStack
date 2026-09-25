<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'
import SettingsToggle from './SettingsToggle.vue'
import {
  DISTANCE_UNITS,
  FUEL_CONSUMPTION_UNITS,
  PRESSURE_UNITS,
  TEMPERATURE_UNITS,
  type UnitPreferences,
} from '@/utils/units'

const { t } = useI18n()
const settings = useUiSettingsStore()
const vehicleStore = useVehicleStore()

interface UnitChoice {
  key: keyof UnitPreferences
  options: readonly string[]
  desc?: string
}

// Fuel consumption means nothing to a car that burns none. While the drivetrain is still unknown
// it is offered, as the dashboard offers its fuel cards then too.
const burnsFuel = computed(() => vehicleStore.effectiveVehicleType !== 'bev')

const choices = computed((): UnitChoice[] => [
  { key: 'distance', options: DISTANCE_UNITS, desc: t('settings.units.distanceDesc') },
  { key: 'temperature', options: TEMPERATURE_UNITS },
  { key: 'pressure', options: PRESSURE_UNITS },
  ...(burnsFuel.value
    ? [
        {
          key: 'fuelConsumption' as const,
          options: FUEL_CONSUMPTION_UNITS,
          desc: t('settings.units.fuelConsumptionDesc'),
        },
      ]
    : []),
])
</script>

<template>
  <div class="settings-toggles">
    <SettingsToggle
      v-for="choice in choices"
      :key="choice.key"
      :label="t(`settings.units.${choice.key}`)"
      :desc="choice.desc"
    >
      <template #control>
        <select
          :id="`settings-unit-${choice.key}`"
          v-model="settings.units[choice.key]"
          class="form-select form-select-sm"
          :aria-label="t(`settings.units.${choice.key}`)"
        >
          <option v-for="unit in choice.options" :key="unit" :value="unit">
            {{ t(`settings.units.options.${unit}`) }}
          </option>
        </select>
      </template>
    </SettingsToggle>
  </div>
</template>
