<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import SettingsToggle from './SettingsToggle.vue'

// One period for every view that filters by date, so the map and the statistics always show
// the same days.
const { t } = useI18n()
const { filterDays } = storeToRefs(useUiSettingsStore())
</script>

<template>
  <SettingsToggle
    :label="t('trips.dateRange')"
    :desc="t('trips.dateRangeDesc')"
    icon="calendar-check"
  >
    <template #control>
      <select v-model="filterDays" class="form-select" :aria-label="t('trips.dateRange')">
        <option :value="7">{{ t('trips.last7days') }}</option>
        <option :value="30">{{ t('trips.last30days') }}</option>
        <option :value="90">{{ t('trips.last90days') }}</option>
      </select>
    </template>
  </SettingsToggle>
</template>
