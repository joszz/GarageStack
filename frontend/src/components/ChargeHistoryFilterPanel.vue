<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  CHARGE_HISTORY_DAYS,
  DEFAULT_CHARGE_HISTORY_DAYS,
  type ChargeHistoryDays,
} from '@/utils/chargeSessions'
import ToolbarPanel from './ToolbarPanel.vue'
import SettingsToggle from './SettingsToggle.vue'

// What narrows the charge history, behind the header's filters button as on the map, the
// statistics and the trip log: the days it covers.
const period = defineModel<ChargeHistoryDays>('period', { required: true })

const { t } = useI18n()

// The badge on the button: the period counts once it is not the one the page starts on.
const activeCount = computed(() => (period.value === DEFAULT_CHARGE_HISTORY_DAYS ? 0 : 1))
</script>

<template>
  <ToolbarPanel :count="activeCount">
    <SettingsToggle
      :label="t('chargeHistory.period')"
      :desc="t('chargeHistory.periodDesc')"
      icon="calendar-check"
    >
      <template #control>
        <select v-model.number="period" class="form-select" :aria-label="t('chargeHistory.period')">
          <option v-for="days in CHARGE_HISTORY_DAYS" :key="days" :value="days">
            {{ t('chargeHistory.lastDays', { days }) }}
          </option>
        </select>
      </template>
    </SettingsToggle>
  </ToolbarPanel>
</template>
