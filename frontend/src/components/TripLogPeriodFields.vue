<script setup lang="ts">
import '@/assets/tripLogPeriod.css'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { formatDate } from '@/utils/format'
import type { TripLogPeriod } from '@/utils/tripLog'

/** The year and month a trip log period is picked by, on the trip log and beside its export. */
const period = defineModel<TripLogPeriod>({ required: true })

const { t } = useI18n()

// Trips are saved from the Worker's first run onwards, which also saves the history before it,
// so a few years back covers any install; there is nothing to page to before that.
const YEARS_OFFERED = 5
const thisYear = new Date().getFullYear()
const years = Array.from({ length: YEARS_OFFERED }, (_, i) => thisYear - i)

const months = computed(() =>
  Array.from({ length: 12 }, (_, m) => ({
    value: m,
    label: formatDate(new Date(2000, m, 1), { month: 'long' }),
  })),
)

const year = computed({
  get: () => period.value.year,
  set: (value: number) => (period.value = { ...period.value, year: value }),
})

const month = computed({
  get: () => period.value.month,
  set: (value: number | null) => (period.value = { ...period.value, month: value }),
})
</script>

<template>
  <div class="trip-log-period">
    <label class="trip-log-period__field">
      <span class="text-xs text-muted">{{ t('tripLog.year') }}</span>
      <select v-model="year" class="form-select">
        <option v-for="y in years" :key="y" :value="y">{{ y }}</option>
      </select>
    </label>
    <label class="trip-log-period__field">
      <span class="text-xs text-muted">{{ t('tripLog.month') }}</span>
      <select v-model="month" class="form-select">
        <option :value="null">{{ t('tripLog.wholeYear') }}</option>
        <option v-for="m in months" :key="m.value" :value="m.value">{{ m.label }}</option>
      </select>
    </label>
  </div>
</template>
