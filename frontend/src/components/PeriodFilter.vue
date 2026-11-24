<script setup lang="ts">
import '@/assets/periodFilter.css'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  addDays,
  daysBetween,
  parseLocalDate,
  resolvePeriod,
  type Period,
  type PeriodOptions,
  type PeriodPreset,
} from '@/utils/period'
import { localDateKey } from '@/utils/dates'
import SettingsToggle from './SettingsToggle.vue'

/**
 * The period row of a page's filters: the same presets and custom dates on every page that filters
 * by date, less what the page cannot load (PeriodOptions), each page keeping its own choice.
 *
 * @param desc What the period decides on this page.
 */
const period = defineModel<Period>({ required: true })
const props = defineProps<{ options: PeriodOptions; desc: string }>()

const { t } = useI18n()
const today = localDateKey(new Date())

const preset = computed({
  get: () => period.value.preset,
  set: (preset: PeriodPreset) => {
    // A custom period starts as the days on screen, so choosing it moves nothing yet, unless
    // they are more than the page can load.
    if (preset === 'custom') {
      const shown = resolvePeriod(period.value)
      const limited = resolvePeriod(
        { preset, from: localDateKey(shown.first), to: localDateKey(shown.last) },
        new Date(),
        props.options.maxDays,
      )
      period.value = { preset, from: localDateKey(limited.first), to: localDateKey(limited.last) }
    } else {
      period.value = { preset, from: null, to: null }
    }
  },
})

// An emptied field keeps the date it had: a period needs both ends. Moving one end further than
// the page can load takes the other end along, rather than refusing the date picked.
function setDate(end: 'from' | 'to', value: string) {
  if (!value) return
  const next = { ...period.value, [end]: value }
  const { maxDays } = props.options
  const from = parseLocalDate(next.from)
  const to = parseLocalDate(next.to)
  if (maxDays && from && to && daysBetween(from, to) > maxDays) {
    if (end === 'from') next.to = localDateKey(addDays(from, maxDays - 1))
    else next.from = localDateKey(addDays(to, 1 - maxDays))
  }
  period.value = next
}
</script>

<template>
  <SettingsToggle :label="t('period.label')" :desc="desc" icon="calendar-check">
    <template #control>
      <select v-model="preset" class="form-select" :aria-label="t('period.label')">
        <option v-for="option in options.presets" :key="option" :value="option">
          {{ t(`period.preset.${option}`) }}
        </option>
      </select>
    </template>
  </SettingsToggle>

  <div v-if="period.preset === 'custom'" class="period-filter__custom">
    <label class="period-filter__field">
      <span class="text-xs text-muted">{{ t('period.from') }}</span>
      <input
        type="date"
        class="form-control"
        :value="period.from"
        :max="period.to ?? today"
        @change="setDate('from', ($event.target as HTMLInputElement).value)"
      />
    </label>
    <label class="period-filter__field">
      <span class="text-xs text-muted">{{ t('period.to') }}</span>
      <input
        type="date"
        class="form-control"
        :value="period.to"
        :min="period.from ?? undefined"
        :max="today"
        @change="setDate('to', ($event.target as HTMLInputElement).value)"
      />
    </label>
    <p v-if="options.maxDays" class="text-xs text-muted period-filter__limit">
      {{ t('period.maxDays', { days: options.maxDays }) }}
    </p>
  </div>
</template>
