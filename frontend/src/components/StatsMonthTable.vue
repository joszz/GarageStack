<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { EnergyUnit } from '@/utils/energy'
import { litres } from '@/utils/energy'
import type { MonthTotal } from '@/utils/statistics'
import { formatDate, formatNumber } from '@/utils/format'
import { useUnits } from '@/composables/useUnits'

const props = defineProps<{
  months: MonthTotal[]
  /** What the energy counter holds on this car, or null when that is not known yet. */
  energyUnit: EnergyUnit | null
}>()

const { t } = useI18n()
const units = useUnits()

const distanceHeader = computed(() =>
  t('statistics.months.distance', { unit: units.value.symbol('distance') }),
)
const energyHeader = computed(() =>
  props.energyUnit === 'litres'
    ? t('statistics.months.fuel', { unit: units.value.symbol('volume') })
    : t('statistics.months.energy'),
)

// The counter in what this page shows it in: kWh as it is, a hybrid's hundredths of a litre as
// litres (or gallons).
function energyText(counter: number | null): string {
  if (counter === null) return '-'
  const fuel = props.energyUnit === 'litres' ? litres(counter) : null
  return formatNumber(fuel !== null ? units.value.convert('volume', fuel) : counter, 1, true)
}

// The header carries the unit, so a cell is the number alone.
function distanceText(km: number): string {
  return units.value.measure('distance', km, { decimals: 0, grouped: true })?.value ?? '-'
}

const rows = computed(() =>
  props.months.map((m) => ({
    key: m.key,
    month: formatDate(m.start, { month: 'long', year: 'numeric' }),
    trips: m.trips,
    distance: distanceText(m.distanceKm),
    energy: energyText(m.counterTotal),
  })),
)

const total = computed(() => {
  const counters = props.months.map((m) => m.counterTotal).filter((c): c is number => c !== null)
  return {
    trips: props.months.reduce((sum, m) => sum + m.trips, 0),
    distance: distanceText(props.months.reduce((sum, m) => sum + m.distanceKm, 0)),
    energy: energyText(counters.length ? counters.reduce((sum, c) => sum + c, 0) : null),
  }
})
</script>

<template>
  <section class="stats-months" :aria-label="t('statistics.months.title')">
    <h2 class="stats-months__title">{{ t('statistics.months.title') }}</h2>
    <table class="stats-months__table">
      <thead>
        <tr>
          <th scope="col">{{ t('statistics.months.month') }}</th>
          <th scope="col" class="stats-months__number">{{ t('statistics.months.trips') }}</th>
          <th scope="col" class="stats-months__number">{{ distanceHeader }}</th>
          <th v-if="energyUnit" scope="col" class="stats-months__number">{{ energyHeader }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.key">
          <th scope="row">{{ row.month }}</th>
          <td class="stats-months__number">{{ row.trips }}</td>
          <td class="stats-months__number">{{ row.distance }}</td>
          <td v-if="energyUnit" class="stats-months__number">{{ row.energy }}</td>
        </tr>
      </tbody>
      <tfoot>
        <tr>
          <th scope="row">{{ t('statistics.months.total') }}</th>
          <td class="stats-months__number">{{ total.trips }}</td>
          <td class="stats-months__number">{{ total.distance }}</td>
          <td v-if="energyUnit" class="stats-months__number">{{ total.energy }}</td>
        </tr>
      </tfoot>
    </table>
  </section>
</template>
