<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { CLOCK_SETTINGS, useUiSettingsStore, type RegionSetting } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'
import { clockExample } from '@/utils/format'
import { REGIONS, regionHourCycle, regionName, type HourCycle } from '@/utils/region'
import { mayBurnFuel } from '@/utils/vehicleType'
import SettingsSelect, { type SettingsSelectOption } from './SettingsSelect.vue'
import {
  DISTANCE_UNITS,
  FUEL_CONSUMPTION_UNITS,
  PRESSURE_UNITS,
  regionUnits,
  TEMPERATURE_UNITS,
  type UnitSettings,
} from '@/utils/units'

const { t, locale } = useI18n()
const settings = useUiSettingsStore()
const vehicleStore = useVehicleStore()

// Every "Automatic" option names what it currently stands for, so a wrong guess is plain to see.
const automatic = (value: string) => t('settings.units.auto', { value })

const regionOptions = computed((): SettingsSelectOption<RegionSetting>[] => {
  const named = REGIONS.map((region) => ({
    value: region,
    label: regionName(region, locale.value),
  }))
  named.sort((a, b) => a.label.localeCompare(b.label, locale.value))
  return [
    { value: 'auto', label: automatic(regionName(settings.detectedRegion, locale.value)) },
    ...named,
  ]
})

const clockLabel = (hourCycle: HourCycle) =>
  t(`settings.units.clockOptions.${hourCycle}`, { example: clockExample(hourCycle) })

const clockOptions = computed(() =>
  CLOCK_SETTINGS.map((clock) => ({
    value: clock,
    label:
      clock === 'auto'
        ? automatic(clockLabel(regionHourCycle(settings.effectiveRegion)))
        : clockLabel(clock),
  })),
)

interface UnitChoice {
  key: keyof UnitSettings
  options: SettingsSelectOption<string>[]
  desc?: string
}

function unitChoice(key: keyof UnitSettings, units: readonly string[], desc?: string): UnitChoice {
  const unitLabel = (unit: string) => t(`settings.units.options.${unit}`)
  const regional = regionUnits(settings.effectiveRegion)[key]
  return {
    key,
    desc,
    options: [
      { value: 'auto', label: automatic(unitLabel(regional)) },
      ...units.map((unit) => ({ value: unit, label: unitLabel(unit) })),
    ],
  }
}

// Fuel consumption means nothing to a car that burns none. While the drivetrain is still unknown
// it is offered, as the dashboard offers its fuel cards then too.
const offersFuelConsumption = computed(() => mayBurnFuel(vehicleStore.effectiveVehicleType))

const unitChoices = computed((): UnitChoice[] => [
  unitChoice('distance', DISTANCE_UNITS, t('settings.units.distanceDesc')),
  unitChoice('temperature', TEMPERATURE_UNITS),
  unitChoice('pressure', PRESSURE_UNITS),
  ...(offersFuelConsumption.value
    ? [
        unitChoice(
          'fuelConsumption',
          FUEL_CONSUMPTION_UNITS,
          t('settings.units.fuelConsumptionDesc'),
        ),
      ]
    : []),
])

// Each select offers its own unit's values only, so what it hands back belongs to its key.
function chooseUnit(key: keyof UnitSettings, value: string) {
  ;(settings.units as Record<keyof UnitSettings, string>)[key] = value
}
</script>

<template>
  <div class="settings-toggles">
    <SettingsSelect
      id="settings-region"
      v-model="settings.region"
      :label="t('settings.units.region')"
      :desc="t('settings.units.regionDesc')"
      :options="regionOptions"
    />
    <SettingsSelect
      id="settings-clock"
      v-model="settings.clock"
      :label="t('settings.units.clock')"
      :options="clockOptions"
    />
    <SettingsSelect
      v-for="choice in unitChoices"
      :id="`settings-unit-${choice.key}`"
      :key="choice.key"
      :model-value="settings.units[choice.key]"
      :label="t(`settings.units.${choice.key}`)"
      :desc="choice.desc"
      :options="choice.options"
      @update:model-value="(value: string) => chooseUnit(choice.key, value)"
    />
  </div>
</template>
