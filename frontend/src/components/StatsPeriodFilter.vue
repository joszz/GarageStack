<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { STATS_PRESETS, resolveStatsPeriod, type StatsPreset } from '@/utils/statsPeriod'
import { localDateKey } from '@/utils/dates'
import SettingsToggle from './SettingsToggle.vue'

// The statistics page's own period. The map keeps its 7, 30 or 90 days (DateRangeFilter): a year
// is fine to sum up but too many trips to draw.
const { t } = useI18n()
const { statsPeriod } = storeToRefs(useUiSettingsStore())
const today = localDateKey(new Date())

const preset = computed({
  get: () => statsPeriod.value.preset,
  set: (preset: StatsPreset) => {
    // A custom period starts as the days on screen, so choosing it moves nothing yet.
    if (preset === 'custom') {
      const shown = resolveStatsPeriod(statsPeriod.value)
      statsPeriod.value = { preset, from: localDateKey(shown.first), to: localDateKey(shown.last) }
    } else {
      statsPeriod.value = { preset, from: null, to: null }
    }
  },
})

// An emptied field keeps the date it had: a period needs both ends.
function setDate(end: 'from' | 'to', value: string) {
  if (value) statsPeriod.value = { ...statsPeriod.value, [end]: value }
}
</script>

<template>
  <SettingsToggle
    :label="t('statistics.period.label')"
    :desc="t('statistics.period.desc')"
    icon="calendar-check"
  >
    <template #control>
      <select v-model="preset" class="form-select" :aria-label="t('statistics.period.label')">
        <option v-for="option in STATS_PRESETS" :key="option" :value="option">
          {{ t(`statistics.period.preset.${option}`) }}
        </option>
      </select>
    </template>
  </SettingsToggle>

  <div v-if="statsPeriod.preset === 'custom'" class="stats-period__custom">
    <label class="stats-period__field">
      <span class="text-xs text-muted">{{ t('statistics.period.from') }}</span>
      <input
        type="date"
        class="form-control"
        :value="statsPeriod.from"
        :max="statsPeriod.to ?? today"
        @change="setDate('from', ($event.target as HTMLInputElement).value)"
      />
    </label>
    <label class="stats-period__field">
      <span class="text-xs text-muted">{{ t('statistics.period.to') }}</span>
      <input
        type="date"
        class="form-control"
        :value="statsPeriod.to"
        :min="statsPeriod.from ?? undefined"
        :max="today"
        @change="setDate('to', ($event.target as HTMLInputElement).value)"
      />
    </label>
  </div>
</template>
