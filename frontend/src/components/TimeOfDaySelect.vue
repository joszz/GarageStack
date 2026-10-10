<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

/**
 * A time of day on a 24-hour clock, as the app's own select boxes rather than the browser's time
 * picker, whose popup no stylesheet reaches.
 */
const props = withDefaults(
  defineProps<{
    /** Given to the hour select, so a label can point at the control. */
    id: string
    minuteStep?: number
  }>(),
  { minuteStep: 5 },
)

/** "HH:mm". */
const model = defineModel<string>({ required: true })

const { t } = useI18n()

const pad = (n: number) => String(n).padStart(2, '0')

const HOURS = Array.from({ length: 24 }, (_, h) => h)

const hour = computed({
  get: () => Number(model.value.slice(0, 2)) || 0,
  set: (h: number) => (model.value = `${pad(h)}:${pad(minute.value)}`),
})

const minute = computed({
  get: () => Number(model.value.slice(3, 5)) || 0,
  set: (m: number) => (model.value = `${pad(hour.value)}:${pad(m)}`),
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
    <select :id="id" v-model.number="hour" class="form-control" :aria-label="t('common.hour')">
      <option v-for="h in HOURS" :key="h" :value="h">{{ pad(h) }}</option>
    </select>
    <span class="time-select__separator" aria-hidden="true">:</span>
    <select v-model.number="minute" class="form-control" :aria-label="t('common.minute')">
      <option v-for="m in minuteOptions" :key="m" :value="m">{{ pad(m) }}</option>
    </select>
  </div>
</template>
