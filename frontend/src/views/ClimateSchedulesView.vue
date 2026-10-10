<script setup lang="ts">
import '@/assets/climateSchedules.css'
import { onMounted, computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useVehicleStore } from '@/stores/vehicle'
import { useClimateSchedulesStore } from '@/stores/climateSchedules'
import StatusCard from '@/components/StatusCard.vue'
import ClimateScheduleFormModal from '@/components/ClimateScheduleFormModal.vue'
import type { ClimateSchedule } from '@/services/climateScheduleApi'
import { daysSummary, needsAttention, runLabel, settingsSummary } from '@/utils/climateSchedule'
import { CLIMATE_MODE_ICONS } from '@/utils/climate'
import { formatTimeOfDay } from '@/utils/format'
import { useUnits } from '@/composables/useUnits'
import { useErrorMessage } from '@/composables/useErrorMessage'

const { t } = useI18n()
const vehicleStore = useVehicleStore()
const store = useClimateSchedulesStore()
const units = useUnits()
const errorMessage = useErrorMessage()

const vin = computed(() => vehicleStore.activeVin)

const formOpen = ref(false)
const editing = ref<ClimateSchedule | null>(null)

async function load() {
  await vehicleStore.fetchVehicles()
  if (vin.value) await store.fetchSchedules(vin.value)
}

onMounted(load)

watch(vin, (v) => {
  if (v) store.fetchSchedules(v)
})

function openAdd() {
  editing.value = null
  formOpen.value = true
}

function openEdit(schedule: ClimateSchedule) {
  editing.value = schedule
  formOpen.value = true
}

function label(schedule: ClimateSchedule): string {
  const days = daysSummary(schedule.days, t)
  return schedule.name ? `${schedule.name} · ${days}` : days
}

// What it sets, then when it runs next: the line a row is told apart by.
function note(schedule: ClimateSchedule): string {
  const when = schedule.nextRunUtc
    ? t('climateSchedules.nextRun', { when: runLabel(schedule.nextRunUtc, new Date(), t) })
    : t('climateSchedules.off')
  return `${settingsSummary(schedule, t, units.value)} · ${when}`
}

function variant(schedule: ClimateSchedule): 'info' | 'warning' | undefined {
  if (!schedule.enabled) return undefined
  return needsAttention(schedule) ? 'warning' : 'info'
}
</script>

<template>
  <div class="view-container">
    <div class="view-header">
      <h1>{{ t('climateSchedules.title') }}</h1>
      <div class="view-header__actions">
        <button class="btn btn-primary btn-sm" :disabled="!vin" @click="openAdd">
          <font-awesome-icon icon="plus" />{{ t('climateSchedules.addSchedule') }}
        </button>
      </div>
    </div>

    <p class="text-muted view-subtitle">{{ t('climateSchedules.subtitle') }}</p>

    <div v-if="store.itemsError" class="empty-state text-danger">
      {{ errorMessage(store.itemsError) }}
    </div>
    <div v-else-if="!store.loading && store.schedules.length === 0" class="empty-state">
      {{ t('climateSchedules.empty') }}
    </div>
    <div v-else class="status-card-list">
      <StatusCard
        v-for="schedule in store.schedules"
        :key="schedule.id"
        :icon="CLIMATE_MODE_ICONS[schedule.mode]"
        :label="label(schedule)"
        :value="formatTimeOfDay(schedule.startTime)"
        :note="note(schedule)"
        :variant="variant(schedule)"
        clickable
        @click="openEdit(schedule)"
      />
    </div>

    <ClimateScheduleFormModal
      v-if="vin"
      :open="formOpen"
      :vin="vin"
      :schedule="editing"
      @close="formOpen = false"
    />
  </div>
</template>
