<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import Multiselect from '@vueform/multiselect'
import Slider from '@vueform/slider'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { DEFAULT_FILTER_DAYS, useUiSettingsStore } from '@/stores/settingsUi'
import { FUEL_TYPES } from '@/utils/fuelTypes'
import ToolbarPanel from '../ToolbarPanel.vue'
import DateRangeFilter from '../DateRangeFilter.vue'

// What narrows the map: the period, and the fuel and charging filters for the POI layers. A
// filter belongs to what the car can use rather than to what is switched on right now, so these
// stay put whether or not their layer is showing; the panel would otherwise look half empty until
// the layer was found in the other one.
const props = defineProps<{
  carTakesFuel: boolean
  carTakesCharge: boolean
  /** The brands the fuel layer knows of, loaded with it. */
  fuelBrands: string[]
  brandsLoading: boolean
}>()

const { t } = useI18n()
const { fuelTypeFilter, fuelBrandFilter, chargingMinPowerKw, chargingMaxPowerKw } =
  storeToRefs(useMapSettingsStore())
const { filterDays } = storeToRefs(useUiSettingsStore())

// Fuel types are a fixed set, unlike brands, so the dropdown is built from the list itself
// rather than from whatever the loaded stations happen to advertise.
const fuelTypeOptions = computed(() =>
  FUEL_TYPES.map((type) => ({ value: type, label: t(`trips.fuelTypes.${type}`) })),
)

// Slider value: [minKw, maxKw] where max=350 means "no upper limit" (stored as 0 in settings)
const powerRangeSlider = computed({
  get: (): [number, number] => [
    chargingMinPowerKw.value,
    chargingMaxPowerKw.value === 0 ? 350 : chargingMaxPowerKw.value,
  ],
  set: (value: number[]) => {
    chargingMinPowerKw.value = value[0]!
    chargingMaxPowerKw.value = (value[1] ?? 350) >= 350 ? 0 : value[1]!
  },
})

const powerRangeLabel = computed(() => {
  const min = chargingMinPowerKw.value
  const max = chargingMaxPowerKw.value
  if (min === 0 && max === 0) return t('trips.chargingPowerAny')
  const minStr = min === 0 ? t('trips.chargingPowerAny') : `${min} kW`
  const maxStr = max === 0 ? '350+ kW' : `${max} kW`
  return `${minStr} - ${maxStr}`
})

function formatPowerTooltip(value: number): string {
  if (value === 0) return t('trips.chargingPowerAny')
  if (value >= 350) return '350+'
  return String(value)
}

// The badge on the panel's button, so the map can be read without opening it. A filter counts
// when it is narrowing something rather than merely having a value, which for the period means
// differing from the one every view starts on.
const activeCount = computed(() => {
  const active = [
    filterDays.value !== DEFAULT_FILTER_DAYS,
    props.carTakesFuel && fuelTypeFilter.value.length > 0,
    props.carTakesFuel && fuelBrandFilter.value.length > 0,
    // One row, so one count, however many of its two ends have been moved off "any".
    props.carTakesCharge && (chargingMinPowerKw.value > 0 || chargingMaxPowerKw.value > 0),
  ]
  return active.filter(Boolean).length
})
</script>

<template>
  <ToolbarPanel :count="activeCount">
    <DateRangeFilter />
    <template v-if="carTakesFuel">
      <div class="poi-filter">
        <div class="poi-filter__header">
          <div class="settings-toggle__info">
            <span class="settings-toggle__label">
              <font-awesome-icon icon="gas-pump" class="settings-toggle__icon" />
              {{ t('trips.fuelTypeFilter') }}
            </span>
            <span class="settings-toggle__desc">{{ t('trips.fuelTypeFilterDesc') }}</span>
          </div>
        </div>
        <Multiselect
          v-model="fuelTypeFilter"
          :options="fuelTypeOptions"
          :placeholder="t('trips.fuelTypePlaceholder')"
          :searchable="false"
          :close-on-select="false"
          :clear-on-select="false"
          mode="tags"
          :no-results-text="t('trips.fuelTypeNoMatch')"
          append-to="body"
          class="fuel-brand-multiselect"
        />
      </div>
      <div class="poi-filter">
        <div class="poi-filter__header">
          <div class="settings-toggle__info">
            <span class="settings-toggle__label">
              <font-awesome-icon icon="tag" class="settings-toggle__icon" />
              {{ t('trips.fuelBrandFilter') }}
            </span>
            <span class="settings-toggle__desc">{{ t('trips.fuelBrandFilterDesc') }}</span>
          </div>
        </div>
        <Multiselect
          v-model="fuelBrandFilter"
          :options="fuelBrands"
          :placeholder="t('trips.fuelBrandPlaceholder')"
          :searchable="true"
          :close-on-select="false"
          :clear-on-select="false"
          mode="tags"
          :loading="brandsLoading"
          :no-results-text="t('trips.fuelBrandNoMatch')"
          :no-options-text="t('trips.fuelBrandNoneLoaded')"
          append-to="body"
          class="fuel-brand-multiselect"
        />
      </div>
    </template>
    <template v-if="carTakesCharge">
      <div class="charging-power-filter">
        <div class="charging-power-filter__header">
          <div>
            <span class="settings-toggle__label">
              <font-awesome-icon icon="bolt" class="settings-toggle__icon" />
              {{ t('trips.chargingPower') }}
            </span>
            <span class="settings-toggle__desc">{{ t('trips.chargingPowerDesc') }}</span>
          </div>
          <span class="charging-power-filter__range">{{ powerRangeLabel }}</span>
        </div>
        <div class="charging-power-filter__slider">
          <Slider
            v-model="powerRangeSlider"
            :min="0"
            :max="350"
            :step="10"
            :tooltips="true"
            :format="formatPowerTooltip"
            :merge="50"
            :lazy="false"
            class="charging-slider"
            :aria-label="[t('trips.chargingMinPower'), t('trips.chargingMaxPower')]"
          />
        </div>
      </div>
    </template>
  </ToolbarPanel>
</template>

<style>
@import url('@vueform/slider/themes/default.css');
</style>

<style scoped>
.charging-power-filter {
  padding: 0.25rem 0 0.5rem;
}

.charging-power-filter__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.9rem;
}

.charging-power-filter__range {
  font-size: 0.75rem;
  color: var(--color-text-muted, #888);
}

.charging-power-filter__slider {
  padding: 0 0.5rem;
}

.charging-slider {
  --slider-connect-bg: #22c55e;
  --slider-tooltip-bg: #22c55e;
  --slider-tooltip-color: #fff;
  --slider-handle-ring-color: rgb(34 197 94 / 20%);
}

/* Shared by the fuel-type and brand filters, which are the same block with a different list. */
.poi-filter {
  padding: 0.25rem 0 0.5rem;
}

.poi-filter__header {
  margin-bottom: 0.5rem;
}
</style>
