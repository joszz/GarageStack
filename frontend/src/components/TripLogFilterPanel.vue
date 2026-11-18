<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { currentMonthPeriod, type TripLogPeriod } from '@/utils/tripLog'
import ToolbarPanel from './ToolbarPanel.vue'
import SettingsToggle from './SettingsToggle.vue'
import TripLogPeriodFields from './TripLogPeriodFields.vue'

// What narrows the trip log, behind the header's filters button as on the map and statistics:
// the month or year it covers, and whether the hidden trips are listed too.
const period = defineModel<TripLogPeriod>('period', { required: true })
const showHidden = defineModel<boolean>('showHidden', { required: true })

const props = defineProps<{
  /** How many trips in the period are hidden; the switch is left out while there are none. */
  hiddenCount: number
}>()

const { t } = useI18n()

// The badge on the button: the period counts once it is not the month the page opens on.
const activeCount = computed(() => {
  const start = currentMonthPeriod()
  const active = [
    period.value.year !== start.year || period.value.month !== start.month,
    showHidden.value && props.hiddenCount > 0,
  ]
  return active.filter(Boolean).length
})
</script>

<template>
  <ToolbarPanel :count="activeCount">
    <SettingsToggle
      class="settings-toggle--stacked"
      :label="t('tripLog.filter.period')"
      :desc="t('tripLog.filter.periodDesc')"
      icon="calendar-check"
    >
      <template #control>
        <TripLogPeriodFields v-model="period" />
      </template>
    </SettingsToggle>
    <SettingsToggle
      v-if="hiddenCount > 0"
      v-model="showHidden"
      :label="t('tripLog.showHidden', hiddenCount)"
      :desc="t('tripLog.filter.hiddenDesc')"
      icon="eye-slash"
    />
  </ToolbarPanel>
</template>
