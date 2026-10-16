<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import CommandButton from './CommandButton.vue'
import CommandFeedback from './CommandFeedback.vue'
import TimeOfDaySelect from './TimeOfDaySelect.vue'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { useCommandDraft } from '@/composables/useCommandDraft'
import { gatewayLabel } from '@/utils/gatewayLabel'
import { timeOfDay } from '@/utils/dates'

// The modes the gateway takes, by the names it reports them under.
const MODES = ['UNTIL_CONFIGURED_TIME', 'UNTIL_CONFIGURED_SOC', 'DISABLED'] as const
type Mode = (typeof MODES)[number]

const COMMAND = 'scheduled-charging'
const DEFAULT_START = '23:00'
const DEFAULT_END = '07:00'

const props = defineProps<{
  vin: string | null
  mode: string | null
  startTime: string | null
  endTime: string | null
  /** Whether the car takes a charge target, and so can charge until it. */
  canChargeUntilTarget: boolean
}>()

const i18n = useI18n()
const { t } = i18n
const { sending, lastResult, isPending, send } = useVehicleCommand()

function isMode(value: string | null): value is Mode {
  return (MODES as readonly (string | null)[]).includes(value)
}

// The schedule the car reports. One it never reported reads as off.
const current = computed(() => ({
  mode: isMode(props.mode) ? props.mode : ('DISABLED' as Mode),
  start: timeOfDay(props.startTime) ?? DEFAULT_START,
  end: timeOfDay(props.endTime) ?? DEFAULT_END,
}))

// The form follows the car, unless a change is on its way to it.
const draft = useCommandDraft(
  () => ({ ...current.value }),
  () => isPending(COMMAND),
)

const modes = computed(() =>
  MODES.filter(
    (mode) =>
      mode !== 'UNTIL_CONFIGURED_SOC' || props.canChargeUntilTarget || current.value.mode === mode,
  ),
)

// Only the times a mode uses count: an off schedule has none, charging until the target no end.
const changed = computed(() => {
  const d = draft.value
  const c = current.value
  if (d.mode !== c.mode) return true
  if (d.mode === 'DISABLED') return false
  return d.start !== c.start || (d.mode === 'UNTIL_CONFIGURED_TIME' && d.end !== c.end)
})

// The gateway reads both times whatever the mode, so they always go along.
function save() {
  const { mode, start, end } = draft.value
  send(props.vin, COMMAND, JSON.stringify({ mode, startTime: start, endTime: end }), {
    isConfirmed: (s) =>
      s.chargingScheduleMode === mode &&
      (mode === 'DISABLED' || timeOfDay(s.chargingScheduleStartTime) === start) &&
      (mode !== 'UNTIL_CONFIGURED_TIME' || timeOfDay(s.chargingScheduleEndTime) === end),
  })
}
</script>

<template>
  <div class="detail-modal__section">
    <div class="detail-modal__section-title">{{ t('control.changeSchedule') }}</div>
    <div class="form-stack">
      <div class="form-group">
        <label for="charging-schedule-mode">{{ t('control.chargingScheduleMode') }}</label>
        <select
          id="charging-schedule-mode"
          v-model="draft.mode"
          class="form-select"
          :disabled="isPending(COMMAND)"
        >
          <option v-for="mode in modes" :key="mode" :value="mode">
            {{ gatewayLabel(i18n, 'vehicle.chargingSession.scheduleModes', mode) }}
          </option>
        </select>
      </div>
      <div v-if="draft.mode !== 'DISABLED'" class="form-row">
        <div class="form-group">
          <label for="charging-schedule-start">{{ t('control.scheduleFrom') }}</label>
          <TimeOfDaySelect id="charging-schedule-start" v-model="draft.start" />
        </div>
        <div v-if="draft.mode === 'UNTIL_CONFIGURED_TIME'" class="form-group">
          <label for="charging-schedule-end">{{ t('control.scheduleUntil') }}</label>
          <TimeOfDaySelect id="charging-schedule-end" v-model="draft.end" />
        </div>
      </div>
      <p v-if="draft.mode !== 'DISABLED'" class="text-muted text-xs">
        {{ t('control.scheduleCarTime') }}
      </p>
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
