<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import ChipGroup from './ChipGroup.vue'
import type { IsoWeekday } from '@/services/climateScheduleApi'
import { WEEKDAYS, WEEKEND, WORKWEEK, weekdayName } from '@/utils/climateSchedule'
import type { ChipOption } from '@/utils/chips'

defineProps<{
  groupLabel: string
  disabled?: boolean
}>()

const model = defineModel<IsoWeekday[]>({ required: true })

const { t } = useI18n()

const dayOptions = computed((): ChipOption<IsoWeekday>[] =>
  WEEKDAYS.map((day) => ({ value: day, label: weekdayName(day) })),
)

// One tap for the common choices; the chips stay to fine-tune them.
const PRESETS: { key: string; days: readonly IsoWeekday[] }[] = [
  { key: 'weekdays', days: WORKWEEK },
  { key: 'weekend', days: WEEKEND },
  { key: 'everyDay', days: WEEKDAYS },
]
</script>

<template>
  <div class="weekday-picker">
    <ChipGroup
      v-model="model"
      :options="dayOptions"
      :group-label="groupLabel"
      :disabled="disabled"
    />
    <div class="weekday-picker__presets">
      <button
        v-for="preset in PRESETS"
        :key="preset.key"
        type="button"
        class="btn btn-sm btn-outline-secondary"
        :disabled="disabled"
        @click="model = [...preset.days]"
      >
        {{ t(`climateSchedules.days.${preset.key}`) }}
      </button>
    </div>
  </div>
</template>
