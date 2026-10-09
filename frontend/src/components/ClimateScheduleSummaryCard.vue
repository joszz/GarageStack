<script setup lang="ts">
import { computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { useClimateSchedulesStore } from '@/stores/climateSchedules'
import { runLabel, runParts, settingsSummary } from '@/utils/climateSchedule'
import { useUnits } from '@/composables/useUnits'
import StatusCard from './StatusCard.vue'

const { t } = useI18n()
const router = useRouter()
const vehicleStore = useVehicleStore()
const store = useClimateSchedulesStore()
const units = useUnits()

const vin = computed(() => vehicleStore.activeVin)

watch(
  vin,
  (v) => {
    if (v) store.fetchSchedules(v)
  },
  { immediate: true },
)

const next = computed(() => store.nextSchedule)

// Time first, as the climate card puts on/off first: on a narrow card the day is what gets cut
// short, and the tooltip says it in full.
const value = computed(() => {
  if (!next.value?.nextRunUtc) return t('climateSchedules.notPlanned')
  const { time, day } = runParts(next.value.nextRunUtc, new Date())
  return day ? `${time} · ${day}` : time
})

const subtitle = computed(() =>
  next.value?.nextRunUtc
    ? `${runLabel(next.value.nextRunUtc, new Date(), t)} · ${settingsSummary(next.value, t, units.value)}`
    : t('climateSchedules.dashboard.add'),
)
</script>

<template>
  <StatusCard
    icon="calendar-days"
    :label="t('climateSchedules.dashboard.label')"
    :value="value"
    :subtitle="subtitle"
    :variant="next ? 'info' : undefined"
    clickable
    @click="router.push({ name: 'climateSchedules' })"
  />
</template>
