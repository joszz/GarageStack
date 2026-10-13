<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import CommandButton from './CommandButton.vue'
import CommandFeedback from './CommandFeedback.vue'
import DetailListSwitch from './DetailListSwitch.vue'
import TimeOfDaySelect from './TimeOfDaySelect.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { timeOfDay } from '@/utils/dates'

const COMMAND = 'battery-heating-schedule'
const DEFAULT_START = '06:00'

const props = defineProps<{
  vin: string | null
  /** The schedule as the gateway reports it: "on" or "off". */
  mode: string | null
  startTime: string | null
}>()

const { t } = useI18n()
const { sending, lastResult, isPending, send } = useVehicleCommand()

const current = computed(() => ({
  on: props.mode === 'on',
  start: timeOfDay(props.startTime) ?? DEFAULT_START,
}))

// The form follows the car, unless a change is on its way to it.
const draft = ref({ ...current.value })
watch(current, (now) => {
  if (!isPending(COMMAND)) draft.value = { ...now }
})

// A switched-off schedule has no time that counts.
const changed = computed(
  () =>
    draft.value.on !== current.value.on ||
    (draft.value.on && draft.value.start !== current.value.start),
)

// The gateway reads the start time even to switch the schedule off, so it always goes along.
function save() {
  const mode = draft.value.on ? 'on' : 'off'
  const start = draft.value.start
  send(props.vin, COMMAND, JSON.stringify({ mode, startTime: start }), {
    isConfirmed: (s) =>
      s.batteryHeatingScheduleMode === mode &&
      (mode === 'off' || timeOfDay(s.batteryHeatingScheduleStartTime) === start),
  })
}
</script>

<template>
  <div class="detail-modal__section">
    <div class="detail-modal__section-title">{{ t('control.changeSchedule') }}</div>
    <div class="form-stack">
      <div class="detail-list">
        <DetailListSwitch
          v-model="draft.on"
          icon="calendar-check"
          :label="t('vehicle.batteryHeating.schedule')"
          :disabled="isPending(COMMAND)"
        />
      </div>
      <div v-if="draft.on" class="form-group">
        <label for="heating-schedule-start">{{ t('vehicle.batteryHeating.startTime') }}</label>
        <TimeOfDaySelect id="heating-schedule-start" v-model="draft.start" />
      </div>
      <p v-if="draft.on" class="text-muted text-xs">{{ t('control.scheduleCarTime') }}</p>
      <CommandButton
        class="btn-primary"
        :pending="isPending(COMMAND)"
        :sending="sending === COMMAND"
        :disabled="!vin || !changed"
        icon="check"
        :label="t('control.saveSchedule')"
        @click="save"
      />
    </div>
    <CommandFeedback :command="COMMAND" :pending="isPending(COMMAND)" :last-result="lastResult" />
  </div>
</template>
