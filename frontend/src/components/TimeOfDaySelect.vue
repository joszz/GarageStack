<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { activeHourCycle, dayPeriodNames } from '@/utils/format'

/**
 * A time of day on the chosen clock, as the app's own select boxes rather than the browser's time
 * picker, whose popup no stylesheet reaches and whose clock follows the browser, not the settings.
 */
const props = withDefaults(
  defineProps<{
    /** Given to the hour select, so a label can point at the control. */
    id: string
    minuteStep?: number
  }>(),
  { minuteStep: 5 },
)

/** "HH:mm", on a 24-hour clock whichever clock is shown. */
const model = defineModel<string>({ required: true })

const { t } = useI18n()

const pad = (n: number) => String(n).padStart(2, '0')

const twelveHour = computed(() => activeHourCycle() === 'h12')

const hour = computed({
  get: () => Number(model.value.slice(0, 2)) || 0,
  set: (h: number) => (model.value = `${pad(h)}:${pad(minute.value)}`),
})

const minute = computed({
  get: () => Number(model.value.slice(3, 5)) || 0,
  set: (m: number) => (model.value = `${pad(hour.value)}:${pad(m)}`),
})

const afternoon = computed({
  get: () => hour.value >= 12,
  set: (pm: boolean) => (hour.value = (hour.value % 12) + (pm ? 12 : 0)),
})

// A 12-hour clock counts 12, 1, ..., 11 in each half of the day, the half chosen beside it.
const shownHour = computed({
  get: () => (twelveHour.value ? hour.value % 12 || 12 : hour.value),
  set: (h: number) => (hour.value = twelveHour.value ? (h % 12) + (afternoon.value ? 12 : 0) : h),
})

const hourOptions = computed(() =>
  twelveHour.value
    ? [12, ...Array.from({ length: 11 }, (_, i) => i + 1)].map((h) => ({ value: h, label: `${h}` }))
    : Array.from({ length: 24 }, (_, h) => ({ value: h, label: pad(h) })),
)

const dayPeriods = computed(() => {
  const names = dayPeriodNames()
  return [
    { value: false, label: names.am },
    { value: true, label: names.pm },
  ]
})

// The steps, plus a stored minute between them, so opening a form never changes a saved time.
const minuteOptions = computed(() => {
  const steps = Array.from(
    { length: Math.ceil(60 / props.minuteStep) },
    (_, i) => i * props.minuteStep,
  )
  return [...new Set([...steps, minute.value])].sort((a, b) => a - b)
})
</script>

<template>
  <div class="time-select">
    <select :id="id" v-model.number="shownHour" class="form-control" :aria-label="t('common.hour')">
      <option v-for="h in hourOptions" :key="h.value" :value="h.value">{{ h.label }}</option>
    </select>
    <span class="time-select__separator" aria-hidden="true">:</span>
    <select v-model.number="minute" class="form-control" :aria-label="t('common.minute')">
      <option v-for="m in minuteOptions" :key="m" :value="m">{{ pad(m) }}</option>
    </select>
    <select
      v-if="twelveHour"
      v-model="afternoon"
      class="form-control"
      :aria-label="t('common.dayPeriod')"
    >
      <option v-for="period in dayPeriods" :key="period.label" :value="period.value">
        {{ period.label }}
      </option>
    </select>
  </div>
</template>
