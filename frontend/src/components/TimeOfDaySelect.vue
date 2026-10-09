<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { intlLocale } from '@/utils/format'
import type { Locale } from '@/stores/settingsShared'

/**
 * A time of day as the app's own select boxes rather than the browser's time picker, whose popup
 * no stylesheet reaches. The hour reads as the interface language's clock: a 12-hour clock gets
 * an AM/PM choice of its own.
 */
const props = withDefaults(
  defineProps<{
    /** Given to the hour select, so a label can point at the control. */
    id: string
    minuteStep?: number
  }>(),
  { minuteStep: 5 },
)

/** "HH:mm", 24-hour, whatever the clock shown. */
const model = defineModel<string>({ required: true })

const { t, locale } = useI18n()

const pad = (n: number) => String(n).padStart(2, '0')
const hours = computed(() => Number(model.value.slice(0, 2)) || 0)
const minutes = computed(() => Number(model.value.slice(3, 5)) || 0)

function set(h: number, m: number) {
  model.value = `${pad(h)}:${pad(m)}`
}

const intl = computed(() => intlLocale(locale.value as Locale))

// A clock that writes 13:00 with AM or PM is a 12-hour one.
const twelveHour = computed(() =>
  new Intl.DateTimeFormat(intl.value, { hour: 'numeric' })
    .formatToParts(new Date(2024, 0, 1, 13))
    .some((part) => part.type === 'dayPeriod'),
)

const isAfternoon = computed(() => hours.value >= 12)

const hourOptions = computed(() =>
  twelveHour.value
    ? [12, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11].map((h) => ({ value: h, label: String(h) }))
    : Array.from({ length: 24 }, (_, h) => ({ value: h, label: pad(h) })),
)

const shownHour = computed({
  get: () => (twelveHour.value ? hours.value % 12 || 12 : hours.value),
  set: (h: number) =>
    set(twelveHour.value ? (h % 12) + (isAfternoon.value ? 12 : 0) : h, minutes.value),
})

// The steps, plus a stored minute between them, so opening a form never changes a saved time.
const minuteOptions = computed(() => {
  const steps = Array.from(
    { length: Math.ceil(60 / props.minuteStep) },
    (_, i) => i * props.minuteStep,
  )
  return [...new Set([...steps, minutes.value])].sort((a, b) => a - b)
})

const minute = computed({
  get: () => minutes.value,
  set: (m: number) => set(hours.value, m),
})

// AM and PM as the locale writes them.
function dayPeriod(hour: number): string {
  const parts = new Intl.DateTimeFormat(intl.value, {
    hour: 'numeric',
    hour12: true,
  }).formatToParts(new Date(2024, 0, 1, hour))
  return parts.find((p) => p.type === 'dayPeriod')?.value ?? (hour < 12 ? 'AM' : 'PM')
}

const period = computed({
  get: () => (isAfternoon.value ? 'pm' : 'am'),
  set: (p: string) => set((hours.value % 12) + (p === 'pm' ? 12 : 0), minutes.value),
})
</script>

<template>
  <div class="time-select">
    <select :id="id" v-model.number="shownHour" class="form-control" :aria-label="t('common.hour')">
      <option v-for="option in hourOptions" :key="option.value" :value="option.value">
        {{ option.label }}
      </option>
    </select>
    <span class="time-select__separator" aria-hidden="true">:</span>
    <select v-model.number="minute" class="form-control" :aria-label="t('common.minute')">
      <option v-for="m in minuteOptions" :key="m" :value="m">{{ pad(m) }}</option>
    </select>
    <select
      v-if="twelveHour"
      v-model="period"
      class="form-control"
      :aria-label="t('common.dayPeriod')"
    >
      <option value="am">{{ dayPeriod(0) }}</option>
      <option value="pm">{{ dayPeriod(12) }}</option>
    </select>
  </div>
</template>
